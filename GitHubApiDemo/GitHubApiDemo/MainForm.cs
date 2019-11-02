﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Octokit;
using GitHubApiDemo.Properties;
using System.Xml;
using System.Threading.Tasks;

namespace GitHubApiDemo
{
    /// <summary>
    /// The main window for this application.
    /// </summary>
    public partial class MainForm : Form
    {
        #region Constructors
        /// <summary>
        /// Create an instance of this form.
        /// </summary>
        public MainForm()
        {
            InitializeComponent();

            mainDataGridView.AutoGenerateColumns = false;

            // Trick to make read-only properties display using regular text color
            // See: https://social.msdn.microsoft.com/Forums/windows/en-US/9fd7591d-8925-43e4-bdf1-988c9bb5ca5e/changing-font-color-on-readonly-fields-in-propertygrid?forum=winforms
            detailPropertyGrid.ViewForeColor = Color.FromArgb(0, 0, 1);
        }
        #endregion // Constructors

        #region Constants
        /// <summary>
        /// A unique name that identifies the client to GitHub.  This should be the name of the
        /// product, GitHub organization, or the GitHub username (in that order of preference) that
        /// is using the Octokit framework.
        ///</summary>
        public static readonly string GitHubIdentity = Assembly
            .GetEntryAssembly()
            .GetCustomAttribute<AssemblyProductAttribute>()
            .Product;
        #endregion // Constants

        #region Private data
        private BackgroundType backgroundType;
        private SearchResult searchResult;
        private Searcher activeSearcher;
        private Searcher searcher;
        private GitHubClient client;
        private User currentUser;
        private object fullDetail;
        private int maximumCount = 1000;
        private int previousCount;
        private bool isExitPending;
        #endregion // Private data

#pragma warning disable IDE1006 
        #region Events
        private void MainForm_FormClosed(object sender, FormClosedEventArgs e) =>
            SaveSettings();

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!mainBackgroundWorker.IsBusy)
                return;

            if (!isExitPending && mainBackgroundWorker.CancellationPending)
                mainBackgroundWorker.CancelAsync();

            isExitPending = true;
            e.Cancel = true;
            return;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            LoadSettings();
            BeginInvoke((MethodInvoker)ShowLoginForm);
        }

        private void dataGridSelectColumnsMenuItem_Click(object sender, EventArgs e) =>
            ShowColumnForm();

        private void detailGetMenuItem_Click(object sender, EventArgs e) =>
            GetFullDetail();

        private void editFindCodeMenuItem_Click(object sender, EventArgs e) =>
            Search<SearchCodeBroker>();

        private void editFindIssueMenuItem_Click(object sender, EventArgs e){
            Search<SearchIssuesBroker>();
        }

        private void editFindLabelMenuItem_Click(object sender, EventArgs e) =>
            Search<SearchLabelsBroker>();

        private void editFindRepositoryMenuItem_Click(object sender, EventArgs e) =>
            Search<SearchRepositoriesBroker>();

        private void editFindUserMenuItem_Click(object sender, EventArgs e) =>
            Search<SearchUsersBroker>();

        private void editSelectColumnsMenuItem_Click(object sender, EventArgs e) =>
            ShowColumnForm();

        private void helpAboutMenuItem_Click(object sender, EventArgs e)
        {
            using (var dialog = new MainAboutBox())
                dialog.ShowDialog(this);
        }

        private void mainBackgroundWorker_DoWork(object sender, DoWorkEventArgs e) =>
            DoWork(e);

        private void mainBackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
        }

        private void mainBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) =>
            CompleteWork(e);

        private void mainDataGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e) =>
            FormatNestedProperty(sender as DataGridView, e);

        private void mainDataGridView_DoubleClick(object sender, EventArgs e) =>
            mainTabControl.SelectedTab = detailTabPage;

        private void mainDataGridView_SelectionChanged(object sender, EventArgs e) =>
            UpdateDetail();

        private void progressTimer_Tick(object sender, EventArgs e) =>
            UpdateProgress();

        private void viewDetailMenuItem_Click(object sender, EventArgs e) =>
            mainTabControl.SelectedTab = detailTabPage;

        private void viewFullDetailMenuItem_Click(object sender, EventArgs e) =>
            GetFullDetail();
        #endregion // Events
#pragma warning restore IDE1006 

        #region Private methods
        private void AddColumns()
        {
            mainDataGridView.DataSource = null;
            mainDataGridView.Columns.Clear();

            DataGridViewColumnCollection columns = mainDataGridView.Columns;

            Type type = searchResult.ItemType;

            foreach (string name in searcher.Columns.Selected)
            {
                PropertyInfo property = ColumnSet.GetProperty(type, name);
                Type propertyType = property.PropertyType;

                columns.Add(new DataGridViewTextBoxColumn
                {
                    DataPropertyName = name,
                    HeaderText = name,
                    Name = name,
                    ValueType = property.PropertyType,
                    Tag = property
                });
            }

            mainDataGridView.DataSource = searchResult.DataSource;

            foreach (DataGridViewColumn column in columns)
            {
                int width = column.Width;
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = width;
            }

            mainTabControl.SelectedTab = listTabPage;
            EnableUI(true);
        }

        private void BeginWork(BackgroundType type, object argument)
        {
            activeSearcher = null;
            fullDetail = null;

            switch (type)
            {
                case BackgroundType.Search:
                    if (argument is Searcher searcher)
                    {
                        mainStatusLabel.Text = $"Searching for {searcher.Type}...";
                        mainProgressBar.Visible = true;
                        mainProgressBar.Value = 0;                        
                        activeSearcher = searcher;
                    }
                    break;

                case BackgroundType.Detail:
                    mainStatusLabel.Text = $"Getting full detail...";
                    break;

                default:
                    return;
            }

            EnableUI(false);
            progressTimer.Start();
            backgroundType = type;
            mainBackgroundWorker.RunWorkerAsync(argument);
        }

        private void CompleteDetail()
        {
            if (fullDetail == null)
            {
                EndWork("Operation failed.");
                return;
            }

            detailPropertyGrid.SelectedObject = new TypeBroker(fullDetail);
            viewFullDetailMenuItem.Enabled = false;
            detailGetMenuItem.Enabled = false;
            fullDetail = null;
            EndWork("Full detail obtained.");
        }

        private void CompleteSearch()
        {
            if (searchResult == null)
            {
                mainDataGridView.DataSource = null;
                EndWork("Operation failed.");
                return;
            }

            AddColumns();

            string incompleteText = searchResult.IncompleteResults ?
                " (incomplete Results)" : string.Empty;

            EndWork($"{searchResult.DataSource.Count} of {searchResult.TotalCount} matches " +
                $"loaded{incompleteText}.");
        }

        private void CompleteWork(RunWorkerCompletedEventArgs e)
        {
            BackgroundType type = backgroundType;
            backgroundType = BackgroundType.None;

            progressTimer.Stop();
            mainProgressBar.Visible = false;
            mainProgressBar.Value = 0;
            activeSearcher = null;
            previousCount = 0;
            EnableUI(true);

            if (isExitPending)
            {
                EndWork("Operation cancelled, closing application...");
                Close();
                return;
            }

            if (e.Error != null)
            {
                MessageBox.Show(this, e.Error.Message, "Warning", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                EndWork($"Error: {e.Error.Message}");
                return;
            }

            if (e.Cancelled)
            {
                EndWork("Operation cancelled.");
                return;
            }

            switch (type)
            {
                case BackgroundType.Search:
                    CompleteSearch();
                    break;

                case BackgroundType.Detail:
                    CompleteDetail();
                    break;

                case BackgroundType.None:
                    EndWork("Operation failed.");
                    break;
            }
        }

        private void CreateClient(Credentials credentials)
        {
            try
            {
                client = new GitHubClient(new ProductHeaderValue(GitHubIdentity));
                if (credentials == null)
                {
                    currentUser = null;
                    return;
                }

                client.Credentials = credentials;
                currentUser = client.User
                    .Current()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                client = null;
                MessageBox.Show(this, ex.Message, "Authentication Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private Searcher CreateSearcher(ISearchBroker broker)
        {
            try
            {
                return broker.CreateSearcher(client, maximumCount);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid Search", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }
        }

        private void DoWork(DoWorkEventArgs e)
        {
            switch (backgroundType)
            {
                case BackgroundType.Search:
                    if (e.Argument is Searcher searcher)
                    {   
                      Search(searcher);
                    }
                    break;

                case BackgroundType.Detail:
                    fullDetail = e.Argument == null ?
                        null : this.searcher.GetDetail(e.Argument);
                    break;

                default:
                    break;
            }

            e.Cancel = mainBackgroundWorker.CancellationPending;
        }

        private void EnableDetailUi(bool isEnabled)
        {
            isEnabled = isEnabled && !mainBackgroundWorker.IsBusy;

            DataGridViewSelectedRowCollection rows = mainDataGridView.SelectedRows;
            object item = rows.Count > 0 ? rows[0].DataBoundItem : null;

            object propertyItem = detailPropertyGrid.SelectedObject is TypeBroker broker ?
                broker.Actual : item;

            bool hasDetail = searcher?.CanGetDetail ?? false;

            bool lacksDetail = isEnabled && hasDetail && rows
                .OfType<DataGridViewRow>()
                .Any(row => row.DataBoundItem == propertyItem);

            viewFullDetailMenuItem.Enabled = lacksDetail;
            detailGetMenuItem.Enabled = lacksDetail;
            viewDetailMenuItem.Enabled = isEnabled;
        }

        private void EnableUI(bool isEnabled)
        {
            isEnabled = isEnabled && !mainBackgroundWorker.IsBusy;

            editSelectColumnsMenuItem.Enabled = isEnabled && mainDataGridView.DataSource != null;
            editFindCodeMenuItem.Enabled = isEnabled;
            editFindLabelMenuItem.Enabled = isEnabled;
            editFindIssueMenuItem.Enabled = isEnabled;
            editFindRepositoryMenuItem.Enabled = isEnabled;
            editFindUserMenuItem.Enabled = isEnabled;
            mainTabControl.Enabled = isEnabled;
            EnableDetailUi(isEnabled);
        }

        private void EndWork(string statusText) =>
            mainStatusLabel.Text = statusText;

        private void FormatNestedProperty(DataGridView grid, DataGridViewCellFormattingEventArgs e)
        {
            if (grid == null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count ||
                e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count)
                return;

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            DataGridViewRow row = grid.Rows[e.RowIndex];
            object item = row.DataBoundItem;

            if (item == null || !column.DataPropertyName.Contains('.'))
                return;

            if (ColumnSet.TryGetNestedPropertyValue(column.DataPropertyName, item, out object value))
                e.Value = value;
        }

        private void GetFullDetail()
        {
            if (searcher != null && (detailPropertyGrid.SelectedObject is TypeBroker broker))
                BeginWork(BackgroundType.Detail, broker.Actual);
        }

        private bool LoadColumnSettings()
        {
            bool isChanged = false;

            CodeSearcher.SavedColumns = ParseColumns(typeof(SearchCode),
                Settings.Default.ColumnsCode, CodeSearcher.DefaultColumns, ref isChanged);
            IssueSearcher.SavedColumns = ParseColumns(typeof(Issue),
                Settings.Default.ColumnsIssue, IssueSearcher.DefaultColumns, ref isChanged);
            LabelSearcher.SavedColumns = ParseColumns(typeof(Octokit.Label),
                Settings.Default.ColumnsLabel, LabelSearcher.DefaultColumns, ref isChanged);
            RepositorySearcher.SavedColumns = ParseColumns(typeof(Repository),
                Settings.Default.ColumnsRepository, RepositorySearcher.DefaultColumns, ref isChanged);
            UserSearcher.SavedColumns = ParseColumns(typeof(User),
                Settings.Default.ColumnsUser, UserSearcher.DefaultColumns, ref isChanged);

            return isChanged;
        }

        private void LoadSettings()
        {
            bool isChanged = UpgradeSettings();

            if (LoadWindowSettings())
            {
                SaveWindowSettings();
                isChanged = true;
            }

            if (LoadColumnSettings())
            {
                SaveColumnSettings();
                isChanged = true;
            }

            if (isChanged)
                Settings.Default.Save();
        }

        private bool LoadWindowSettings()
        {
            if (Settings.Default.WindowSize == Size.Empty)
                return true;

            Size = Settings.Default.WindowSize;
            Location = Settings.Default.WindowLocation;
            WindowState = Settings.Default.WindowState;
            return false;
        }

        private ColumnSet ParseColumns(Type type, string columns, ColumnSet defaultColumns,
            ref bool isChanged)
        {
            try
            {
                List<string> selectedColumns = columns
                    .Split(',')
                    .Where(column => !string.IsNullOrWhiteSpace(column))
                    .ToList();

                if (selectedColumns.Count > 0)
                {
                    ColumnSet result = new ColumnSet(type, Searcher.Depth, selectedColumns);
                    isChanged |= columns != result.ToString();
                    return result;
                }
            }
            catch
            {
            }

            isChanged = true;
            return defaultColumns;
        }

        private void SaveColumnSettings()
        {
            Settings.Default.ColumnsCode = CodeSearcher.SavedColumns.ToString();
            Settings.Default.ColumnsIssue = IssueSearcher.SavedColumns.ToString();
            Settings.Default.ColumnsLabel = LabelSearcher.SavedColumns.ToString();
            Settings.Default.ColumnsRepository = RepositorySearcher.SavedColumns.ToString();
            Settings.Default.ColumnsUser = UserSearcher.SavedColumns.ToString();
        }

        private void SaveSettings()
        {
            SaveWindowSettings();
            SaveColumnSettings();
            Settings.Default.Save();
        }

        private void SaveWindowSettings()
        {
            if (WindowState == FormWindowState.Normal)
            {
                Settings.Default.WindowLocation = Location;
                Settings.Default.WindowSize = Size;
            }

            if (WindowState != FormWindowState.Minimized)
                Settings.Default.WindowState = WindowState;
        }

        private void Search<TBroker>()
            where TBroker : ISearchBroker, new()
        {
            var broker = new TBroker();
            using (var dialog = new SearchCriteriaForm { SelectedObject = broker })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    BeginWork(BackgroundType.Search, CreateSearcher(broker));
        }

        private void Search(Searcher searcher)
        {
             searchResult = searcher.Search();
             this.searcher = searcher;
        }

        private void ShowColumnForm()
        {
            using (var dialog = new ColumnForm { InitialColumns = searcher.Columns })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    searcher.Columns = dialog.Columns;
                    AddColumns();
                    SaveColumnSettings();
                    Settings.Default.Save();
                }
            }
        }

        private void ShowLoginForm()
        {
            while (client == null)
                using (var dialog = new LoginForm())
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        CreateClient(dialog.Credentials);
                        GetIssueDetails();
                    }
                    else
                    {
                        Close();
                        return;
                    }

            EnableUI(true);
        }

        private void UpdateDetail()
        {
            DataGridViewSelectedRowCollection rows = mainDataGridView.SelectedRows;
            object item = rows.Count > 0 ? rows[0].DataBoundItem : null;

            detailPropertyGrid.SelectedObject = item == null ?
                null : new TypeBroker(item);

            EnableDetailUi(true);
        }

        private void UpdateProgress()
        {
            if (!mainBackgroundWorker.IsBusy || activeSearcher == null)
                return;

            string type = activeSearcher.Type;

            int? totalCount = activeSearcher.TotalCount;
            if (!totalCount.HasValue)
                return;

            int count = activeSearcher.Count ?? 0;
            if (count == previousCount)
                return;

            int percent = totalCount.Value == 0 ? 0 : count * 100 / totalCount.Value;
            if (mainProgressBar.Value != percent)
                mainProgressBar.Value = percent;

            mainStatusLabel.Text = $"Searching for {type} ({count} of {totalCount.Value} results)...";
            previousCount = count;
        }

        private bool UpgradeSettings()
        {
            if (Settings.Default.IsUpgraded)
                return false;

            Settings.Default.Upgrade();
            Settings.Default.IsUpgraded = true;
            return true;
        }

        private async void GetIssues()
        {
            string owner = "dotnet";
            string name = "roslyn";

            try
            {
                string issuseIDs = "";
                List<string> issueslists = new List<string>();
                issueslists = issuseIDs.ToString().Split(',').ToList();

                #region Issues 

                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load("C:\\PhD\\Workbrench\\RoslynIssuesLabels.xml");
                XmlNode rootNode = xmlDoc["Issues"];

                foreach (var item in issueslists)
                {
                    var task = GetIssue(owner, name, Convert.ToInt32(item));
                    if (!task.IsCompleted)
                    {
                        var response = await task.ConfigureAwait(false);
                        Issue issue = response as Issue;

                        //if (issue != null)
                        //{
                        //    XmlNode IssueNode = xmlDoc.CreateElement("Issue");
                        //    XmlElement elemID = xmlDoc.CreateElement("IssueID");
                        //    elemID.InnerText = issue.Number.ToString();
                        //    IssueNode.AppendChild(elemID);

                        //    XmlElement elemRepoID = xmlDoc.CreateElement("RepoID");
                        //    elemRepoID.InnerText = 1.ToString();
                        //    IssueNode.AppendChild(elemRepoID);

                        //    XmlElement elemTitle = xmlDoc.CreateElement("Title");
                        //    elemTitle.InnerText = issue.Title?.ToString();
                        //    IssueNode.AppendChild(elemTitle);

                        //    XmlElement elemDescription = xmlDoc.CreateElement("Description");
                        //    elemDescription.InnerText = issue.Body?.ToString();
                        //    IssueNode.AppendChild(elemDescription);

                        //    XmlElement elemOpenedAt = xmlDoc.CreateElement("CreatedDate");
                        //    elemOpenedAt.InnerText = issue.CreatedAt.ToString("dd/MM/yyyy");
                        //    IssueNode.AppendChild(elemOpenedAt);

                        //    XmlElement elemClosedAt = xmlDoc.CreateElement("ClosedDate");
                        //    elemClosedAt.InnerText = issue.ClosedAt?.ToString("dd/MM/yyyy");
                        //    IssueNode.AppendChild(elemClosedAt);

                        //    rootNode.AppendChild(IssueNode);
                        //}

                    }
                }


                xmlDoc.Save("C:\\PhD\\Workbrench\\RoslynIssuesLabels.xml");
                MessageBox.Show("Done");
                #endregion Issues


            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private async void GetIssueDetails()
        {
            //string owner = "dotnet";
            //string name = "roslyn";

            string owner = "dotnet";
            string name = "roslyn";

           // string owner = "SVF-tools";
            //string name = "SVF";

            try
            {
                var Contributors = GetContributors(owner, name);
                if (!Contributors.IsCompleted)
                {
                    var collaboratorresponse = await Contributors.ConfigureAwait(false);
                    IReadOnlyList<RepositoryContributor> _collaboratorresponse = collaboratorresponse as IReadOnlyList<RepositoryContributor>;
                    List<string> collaborators = _collaboratorresponse?.Select(x => x.Login).ToList();

                    string issuedIDs = "12286,12285,12284,12283,12282,12281,12280,12279,12278,12277,12276,12275,12274,12273,12272,12271,12270,12269,12268,12267,12266,12265,12264,12263,12262,12261,12260,12259,12258,12257,12256,12255,12254,12253,12252,12251,12250,12249,12248,12247,12246,12245,12244,12243,12242,12241,12240,12239,12238,12237,12236,12235,12234,12233,12232,12231,12230,12229,12228,12227,12226,12225,12224,12223,12222,12221,12220,12219,12218,12217,12216,12215,12214,12213,12212,12211,12210,12209,12208,12207,12206,12205,12204,12203,12202,12201,12200,12199,12198,12197,12196,12195,12194,12193,12192,12191,12190,12189,12188,12187,12186,12185,12184,12183,12182,12181,12180,12179,12178,12177,12176,12175,12174,12173,12172,12171,12170,12169,12168,12167,12166,12165,12164,12163,12162,12161,12160,12159,12158,12157,12156,12155,12154,12153,12152,12151,12150,12149,12148,12147,12146,12145,12144,12143,12142,12141,12140,12139,12138,12137,12136,12135,12134,12133,12132,12131,12130,12129,12128,12127,12126,12125,12124,12123,12122,12121,12120,12119,12118,12117,12116,12115,12114,12113,12112,12111,12110,12109,12108,12107,12106,12105,12104,12103,12102,12101,12100,12099,12098,12097,12096,12095,12094,12093,12092,12091,12090,12089,12088,12087,12086,12085,12084,12083,12082,12081,12080,12079,12078,12077,12076,12075,12074,12073,12072,12071,12069,12068,12067,12066,12065,12064,12063,12062,12061,12060,12059,12058,12057,12056,12055,12054,12053,12052,12051,12050,12049,12048,12047,12046,12045,12044,12043,12042,12041,12040,12039,12038,12037,12036,12035,12034,12033,12032,12031,12030,12029,12028,12027,12026,12025,12024,12023,12022,12021,12020,12019,12018,12017,12016,12015,12014,12013,12012,12011,12010,12009,12008,12007,12006,12005,12004,12003,12002,12001,12000,11999,11998,11997,11996,11995,11994,11993,11992,11991,11990,11989,11988,11987,11986,11985,11984,11983,11982,11981,11980,11979,11978,11977,11976,11975,11974,11973,11972,11971,11970,11969,11968,11967,11966,11965,11964,11963,11962,11961,11960,11959,11958,11957,11956,11955,11954,11953,11952,11951,11950,11949,11948,11947,11946,11945,11944,11943,11942,11941,11940,11939,11938,11937,11936,11935,11934,11933,11932,11931,11930,11929,11928,11927,11926,11925,11924,11923,11922,11921,11920,11919,11918,11917,11916,11915,11914,11913,11912,11911,11910,11909,11908,11907,11906,11905,11904,11903,11902,11901,11900,11899,11898,11897,11896,11895,11894,11893,11892,11891,11890,11889,11888,11887,11886,11885,11884,11883,11880,11881,11879,11878,11877,11876,11875,11874,11873,11872,11871,11870,11869,11868,11867,11866,11865,11864,11863,11862,11861,11860,11859,11858,11857,11856,11855,11854,11853,11852,11851,11850,11849,11848,11847,11846,11845,11844,11843,11842,11841,11840,11839,11838,11837,11836,11835,11834,11833,11832,11831,11830,11829,11828,11827,11826,11825,11824,11823,11822,11821,11820,11819,11818,11817,11816,11815,11814,11813,11812,11811,11810,11809,11808,11807,11806,11805,11804,11803,11802,11801,11800,11799,11798,11797,11796,11795,11794,11793,11792,11791,11790,11789,11788,11787,11786,11785,11784,11783,11782,11781,11780,11779,11778,11777,11776,11775,11774,11773,11772,11771,11770,11769,11768,11767,11766,11765,11764,11763,11762,11761,11760,11759,11758,11757,11756,11755,11754,11753,11752,11751,11750,11749,11748,11747,11746,11745,11744,11743,11742,11741,11740,11739,11738,11737,11736,11735,11734,11733,11732,11731,11730,11729,11728,11727,11726,11725,11724,11723,11722,11721,11720,11719,11718,11717,11716,11715,11714,11713,11712,11711,11710,11709,11708,11707,11706,11705,11704,11703,11702,11701,11700,11699,11698,11697,11696,11695,11694,11693,11692,11691,11690,11689,11688,11687,11686,11685,11684,11683,11682,11681,11680,12846,12845,12844,12843,12842,12841,12840,12839,12838,12837,12836,12835,12834,12833,12832,12831,12830,12829,12828,12827,12826,12825,12824,12823,12822,12821,12820,12819,12818,12817,12816,12815,12814,12813,12812,12811,12810,12809,12808,12807,12806,12805,12804,12803,12802,12800,12799,12798,12797,12796,12795,12794,12793,12792,12791,12790,12789,12788,12787,12786,12785,12784,12783,12782,12781,12780,12779,12778,12777,12776,12775,12774,12773,12772,12771,12770,12769,12768,12767,12766,12765,12764,12763,12762,12761,12760,12759,12758,12757,12756,12755,12754,12753,12752,12751,12750,12749,12748,12747,12746,12745,12744,12743,12742,12741,12740,12739,12738,12737,12736,12735,12734,12733,12732,12731,12730,12729,12728,12727,12726,12725,12724,12723,12722,12721,12720,12719,12718,12717,12716,12715,12714,12713,12712,12711,12710,12709,12708,12707,12706,12705,12704,12703,12702,12701,12700,12699,12698,12697,12696,12695,12694,12693,12692,12691,12690,12689,12688,12687,12686,12685,12684,12683,12682,12681,12680,12679,12678,12677,12676,12675,12674,12673,12672,12671,12670,12669,12668,12667,12666,12665,12664,12663,12662,12661,12660,12659,12658,12657,12656,12655,12654,12653,12652,12651,12650,12649,12648,12647,12646,12645,12644,12643,12642,12641,12640,12639,12638,12637,12636,12635,12634,12633,12632,12631,12630,12629,12628,12627,12626,12625,12624,12623,12622,12621,12620,12619,12618,12617,12616,12615,12614,12613,12612,12611,12610,12609,12608,12607,12606,12605,12604,12603,12602,12601,12600,12599,12598,12597,12596,12595,12594,12593,12592,12591,12590,12589,12588,12587,12586,12585,12584,12583,12582,12581,12580,12579,12578,12577,12576,12575,12574,12573,12572,12571,12570,12569,12568,12567,12566,12565,12564,12563,12562,12561,12560,12559,12558,12557,12556,12555,12554,12553,12552,12551,12550,12549,12548,12547,12546,12545,12544,12543,12542,12541,12540,12539,12538,12537,12536,12535,12534,12533,12532,12531,12530,12529,12528,12527,12526,12525,12524,12523,12522,12521,12520,12519,12518,12517,12516,12515,12514,12513,12512,12511,12510,12509,12508,12507,12506,12505,12504,12503,12502,12501,12500,12499,12498,12497,12496,12495,12494,12493,12492,12491,12490,12489,12488,12487,12486,12485,12484,12483,12482,12481,12480,12479,12478,12477,12476,12475,12474,12473,12472,12471,12470,12469,12468,12467,12466,12465,12464,12463,12462,12461,12460,12459,12458,12457,12456,12455,12454,12453,12452,12451,12450,12449,12448,12447,12446,12445,12444,12443,12442,12441,12440,12439,12438,12437,12436,12435,12434,12433,12432,12431,12430,12429,12428,12427,12426,12425,12424,12423,12422,12421,12420,12419,12418,12417,12416,12415,12414,12413,12412,12411,12410,12409,12408,12407,12406,12405,12404,12403,12402,12401,12400,12399,12398,12397,12396,12395,12394,12393,12392,12391,12390,12389,12388,12387,12386,12385,12384,12383,12382,12381,12380,12379,12378,12377,12376,12375,12374,12373,12372,12371,12370,12369,12368,12367,12366,12365,12364,12363,12362,12361,12360,12359,12358,12357,12356,12355,12354,12353,12352,12351,12350,12349,12348,12347,12346,12846,12845,12844,12843,12842,12841,12840,12839,12838,12837,12836,12835,12834,12833,12832,12831,12830,12829,12828,12827,12826,12825,12824,12823,12822,12821,12820,12819,12818,12817,12816,12815,12814,12813,12812,12811,12810,12809,12808,12807,12806,12805,12804,12803,12802,12800,12799,12798,12797,12796,12795,12794,12793,12792,12791,12790,12789,12788,12787,12786,12785,12784,12783,12782,12781,12780,12779,12778,12777,12776,12775,12774,12773,12772,12771,12770,12769,12768,12767,12766,12765,12764,12763,12762,12761,12760,12759,12758,12757,12756,12755,12754,12753,12752,12751,12750,12749,12748,12747,12746,12745,12744,12743,12742,12741,12740,12739,12738,12737,12736,12735,12734,12733,12732,12731,12730,12729,12728,12727,12726,12725,12724,12723,12722,12721,12720,12719,12718,12717,12716,12715,12714,12713,12712,12711,12710,12709,12708,12707,12706,12705,12704,12703,12702,12701,12700,12699,12698,12697,12696,12695,12694,12693,12692,12691,12690,12689,12688,12687,12686,12685,12684,12683,12682,12681,12680,12679,12678,12677,12676,12675,12674,12673,12672,12671,12670,12669,12668,12667,12666,12665,12664,12663,12662,12661,12660,12659,12658,12657,12656,12655,12654,12653,12652,12651,12650,12649,12648,12647,12646,12645,12644,12643,12642,12641,12640,12639,12638,12637,12636,12635,12634,12633,12632,12631,12630,12629,12628,12627,12626,12625,12624,12623,12622,12621,12620,12619,12618,12617,12616,12615,12614,12613,12612,12611,12610,12609,12608,12607,12606,12605,12604,12603,12602,12601,12600,12599,12598,12597,12596,12595,12594,12593,12592,12591,12590,12589,12588,12587,12586,12585,12584,12583,12582,12581,12580,12579,12578,12577,12576,12575,12574,12573,12572,12571,12570,12569,12568,12567,12566,12565,12564,12563,12562,12561,12560,12559,12558,12557,12556,12555,12554,12553,12552,12551,12550,12549,12548,12547,12546,12545,12544,12543,12542,12541,12540,12539,12538,12537,12536,12535,12534,12533,12532,12531,12530,12529,12528,12527,12526,12525,12524,12523,12522,12521,12520,12519,12518,12517,12516,12515,12514,12513,12512,12511,12510,12509,12508,12507,12506,12505,12504,12503,12502,12501,12500,12499,12498,12497,12496,12495,12494,12493,12492,12491,12490,12489,12488,12487,12486,12485,12484,12483,12482,12481,12480,12479,12478,12477,12476,12475,12474,12473,12472,12471,12470,12469,12468,12467,12466,12465,12464,12463,12462,12461,12460,12459,12458,12457,12456,12455,12454,12453,12452,12451,12450,12449,12448,12447,12446,12445,12444,12443,12442,12441,12440,12439,12438,12437,12436,12435,12434,12433,12432,12431,12430,12429,12428,12427,12426,12425,12424,12423,12422,12421,12420,12419,12418,12417,12416,12415,12414,12413,12412,12411,12410,12409,12408,12407,12406,12405,12404,12403,12402,12401,12400,12399,12398,12397,12396,12395,12394,12393,12392,12391,12390,12389,12388,12387,12386,12385,12384,12383,12382,12381,12380,12379,12378,12377,12376,12375,12374,12373,12372,12371,12370,12369,12368,12367,12366,12365,12364,12363,12362,12361,12360,12359,12358,12357,12356,12355,12354,12353,12352,12351,12350,12349,12348,12347,12346,12345,12344,12343,12342,12341,12340,12339,12338,12337,12336,12335,12334,12333,12332,12331,12330,12329,12328,12327,12326,12325,12324,12323,12322,12321,12320,12319,12318,12317,12316,12315,12314,12313,13514,13513,13512,13511,13510,13509,13508,13507,13506,13505,13504,13503,13502,13501,13500,13499,13498,13497,13496,13495,13494,13493,13492,13491,13490,13489,13488,13487,13486,13485,13484,13483,13482,13481,13480,13479,13478,13477,13476,13475,13474,13473,13472,13471,13470,13469,13468,13467,13466,13465,13464,13463,13462,13461,13460,13459,13458,13457,13456,13455,13454,13453,13452,13451,13450,13449,13448,13447,13446,13445,13444,13443,13442,13441,13440,13439,13438,13437,13436,13435,13434,13433,13432,13431,13430,13429,13428,13427,13426,13425,13424,13423,13422,13421,13420,13419,13418,13417,13416,13415,13414,13413,13412,13411,13410,13409,13408,13407,13406,13405,13404,13403,13402,13400,13401,13399,13398,13397,13396,13395,13394,13393,13392,13391,13390,13389,13388,13387,13386,13385,13384,13383,13382,13381,13380,13379,13378,13377,13376,13375,13374,13373,13372,13371,13370,13369,13368,13367,13366,13365,13364,13363,13362,13361,13360,13359,13358,13357,13356,13355,13354,13353,13352,13351,13350,13349,13348,13347,13346,13345,13344,13343,13342,13341,13340,13339,13338,13337,13336,13335,13334,13333,13332,13331,13330,13329,13328,13327,13326,13325,13324,13323,13322,13321,13320,13319,13318,13317,13316,13315,13314,13313,13312,13311,13310,13309,13308,13307,13306,13305,13304,13303,13302,13301,13300,13299,13298,13297,13296,13295,13294,13293,13292,13291,13290,13289,13288,13287,13286,13285,13284,13283,13282,13281,13279,13278,13277,13276,13275,13274,13273,13272,13271,13270,13269,13268,13267,13266,13265,13264,13263,13262,13261,13260,13259,13258,13257,13256,13255,13254,13253,13252,13251,13250,13249,13248,13247,13246,13244,13243,13242,13241,13240,13239,13238,13237,13236,13235,13234,13233,13232,13231,13230,13229,13228,13227,13226,13225,13224,13223,13222,13221,13220,13219,13218,13217,13216,13215,13214,13213,13212,13211,13210,13209,13208,13207,13206,13205,13204,13203,13202,13201,13200,13199,13198,13197,13196,13195,13194,13193,13192,13191,13190,13189,13188,13187,13186,13185,13184,13183,13182,13181,13180,13179,13178,13177,13176,13175,13174,13173,13172,13171,13170,13169,13168,13167,13166,13165,13164,13163,13162,13161,13160,13159,13158,13157,13156,13155,13154,13153,13152,13151,13150,13149,13148,13147,13146,13145,13144,13143,13142,13141,13140,13139,13138,13137,13136,13135,13134,13133,13132,13131,13130,13129,13128,13127,13126,13125,13124,13123,13122,13121,13120,13119,13118,13117,13116,13115,13114,13113,13112,13111,13110,13109,13108,13107,13106,13105,13104,13103,13102,13101,13100,13099,13098,13097,13096,13095,13094,13093,13092,13091,13090,13089,13088,13087,13086,13085,13084,13083,13082,13081,13080,13079,13078,13077,13076,13075,13074,13073,13072,13071,13070,13069,13068,13067,13066,13065,13064,13063,13062,13061,13060,13059,13058,13057,13056,13055,13054,13053,13052,13051,13050,13049,13048,13047,13046,13045,13044,13043,13042,13041,13040,13039,13038,13037,13036,13035,13034,13033,13032,13031,13030,13029,13028,13027,13026,13025,13024,13023,13022,13021,13020,13019,13018,13017,13016,13015,13014,13013,13012,13011,13010,13009,13008,13007,13006,13005,13004,13003,13002,13001,13000,12999,12998,12997,12996,12995,12994,12993,12992,12991,12990,12989,12988,12987,12986,12985,12984,12983,12982,12981,12980,12979,12978,12977,12976,12975,12974,12973,12972,12971,12970,12969,12968,12967,12966,12965,12964,12963,12962,12961,12960,12959,12958,12957,12956,12955,12954,12953,12952,12951,12950,12949,12948,12947,12946,12945,12944,12943,12942,12941,12940,12939,12938,12937,12936,12935,12934,12933,12932,12931,12930,12929,12928,12927,12926,12925,12924,12923,12922,12921,12920,12919,12918,12917,12916,12915,12914,12913,12912,12911,12910,12909,12908,12907,12906,12905,12904,12903,12902,12901,12900,12899,12898,12897,12896,12895,12894,12893,12892,12891,12890,12889,12888,12887,12886,12885,12884,12883,12882,12881,12880,12879,12878,12877,12876,12875,12874,12873,12872,12871,12870,12869,12868,12867,12866,12865,12864,12863,12862,12861,12860,12859,12858,12857,12856,12855,12854,12853,12852,12851,12850,12849,12848,12847,14221,14220,14219,14218,14217,14216,14215,14214,14213,14212,14211,14210,14209,14208,14206,14205,14204,14203,14202,14201,14200,14199,14198,14197,14196,14195,14194,14193,14192,14191,14190,14189,14188,14187,14186,14185,14184,14183,14182,14181,14180,14179,14178,14177,14176,14175,14174,14173,14172,14171,14170,14169,14168,14167,14166,14165,14164,14163,14162,14161,14160,14159,14158,14157,14156,14155,14154,14153,14152,14151,14150,14149,14148,14147,14146,14145,14144,14143,14142,14141,14140,14139,14138,14137,14136,14135,14134,14133,14132,14131,14130,14129,14128,14127,14126,14125,14124,14123,14122,14121,14120,14119,14118,14117,14116,14115,14114,14113,14112,14111,14110,14109,14108,14107,14106,14105,14104,14103,14102,14101,14100,14099,14098,14097,14096,14095,14094,14093,14092,14091,14090,14089,14088,14087,14086,14085,14084,14083,14082,14081,14080,14079,14078,14077,14076,14075,14074,14073,14072,14071,14070,14069,14068,14067,14066,14065,14064,14063,14062,14061,14060,14059,14058,14057,14056,14055,14054,14053,14052,14051,14050,14049,14048,14047,14046,14045,14044,14043,14042,14041,14040,14039,14038,14037,14036,14035,14034,14033,14032,14031,14030,14029,14028,14027,14026,14025,14024,14023,14022,14021,14020,14019,14018,14017,14016,14015,14013,14012,14011,14010,14009,14008,14007,14006,14005,14004,14003,14002,13999,13998,13997,13996,13995,13994,13993,13992,13991,13990,13989,13987,13986,13985,13984,13983,13981,13980,13979,13978,13977,13976,13975,13974,13973,13972,13971,13970,13969,13968,13967,13966,13965,13964,13963,13962,13961,13960,13959,13958,13957,13956,13955,13954,13953,13952,13951,13950,13949,13948,13947,13946,13945,13944,13943,13942,13941,13940,13939,13938,13937,13936,13935,13934,13933,13932,13931,13930,13929,13928,13927,13926,13925,13924,13923,13922,13921,13920,13919,13918,13917,13916,13915,13914,13913,13912,13911,13910,13909,13908,13907,13906,13905,13904,13903,13902,13901,13900,13899,13898,13897,13896,13895,13894,13893,13892,13891,13890,13889,13888,13887,13886,13885,13884,13883,13882,13881,13879,13878,13877,13876,13875,13874,13873,13872,13871,13870,13869,13868,13867,13866,13865,13864,13863,13862,13861,13860,13859,13858,13857,13856,13855,13854,13853,13852,13851,13850,13849,13848,13847,13846,13845,13844,13843,13842,13841,13840,13839,13838,13837,13836,13835,13834,13833,13832,13831,13830,13829,13828,13827,13826,13825,13824,13823,13822,13821,13820,13819,13818,13817,13816,13815,13814,13813,13812,13811,13810,13809,13808,13807,13806,13805,13804,13803,13802,13801,13800,13799,13798,13797,13796,13795,13794,13793,13792,13791,13790,13789,13788,13787,13786,13785,13784,13783,13782,13781,13780,13779,13778,13777,13776,13775,13774,13773,13772,13771,13770,13769,13768,13767,13766,13765,13764,13763,13762,13761,13760,13759,13758,13757,13756,13755,13754,13753,13752,13751,13750,13749,13748,13747,13746,13745,13744,13743,13742,13741,13740,13739,13738,13737,13736,13735,13734,13733,13732,13731,13730,13729,13728,13727,13726,13725,13724,13723,13722,13721,13720,13719,13718,13717,13716,13715,13714,13713,13712,13711,13710,13709,13708,13707,13706,13705,13704,13703,13702,13701,13700,13699,13698,13697,13696,13695,13694,13693,13692,13691,13690,13689,13688,13687,13686,13685,13684,13683,13682,13681,13680,13679,13678,13677,13676,13675,13674,13673,13672,13671,13670,13669,13668,13667,13666,13665,13664,13663,13662,13661,13660,13659,13658,13657,13656,13655,13654,13653,13652,13651,13650,13649,13648,13647,13646,13645,13644,13643,13642,13641,13640,13639,13638,13637,13636,13635,13634,13633,13632,13631,13630,13629,13628,13627,13626,13625,13624,13623,13622,13621,13620,13619,13618,13617,13616,13615,13614,13613,13612,13611,13610,13609,13608,13607,13606,13605,13604,13603,13602,13601,13600,13599,13598,13597,13596,13595,13594,13593,13592,13591,13590,13589,13588,13587,13586,13585,13584,13583,13582,13581,13580,13579,13578,13577,13576,13575,13574,13573,13572,13571,13570,13569,13568,13567,13566,13565,13564,13563,13562,13561,13560,13559,13558,13557,13556,13555,13554,13553,13552,13551,13550,13549,13548,13547,13546,13545,13544,13543,13542,13541,13540,13539,13538,13537,13536,13535,13534,13533,13532,13531,13530,13529,13528,13527,13526,13525,13523,13522,13521,13520,13519,13518,13517,13516,13515,14843,14842,14841,14840,14839,14838,14837,14836,14835,14834,14833,14832,14831,14830,14829,14828,14827,14826,14825,14824,14823,14822,14821,14820,14819,14818,14817,14816,14815,14814,14813,14812,14811,14810,14809,14808,14807,14806,14805,14804,14803,14802,14801,14800,14799,14798,14797,14796,14795,14794,14793,14792,14791,14790,14789,14788,14787,14786,14785,14784,14783,14782,14781,14780,14779,14778,14777,14776,14775,14774,14773,14772,14771,14770,14769,14768,14767,14766,14765,14764,14763,14762,14761,14760,14759,14758,14757,14756,14755,14754,14753,14752,14751,14750,14749,14748,14747,14746,14745,14744,14743,14742,14741,14740,14739,14738,14737,14736,14735,14734,14733,14732,14731,14730,14729,14728,14727,14725,14724,14723,14722,14721,14720,14719,14718,14717,14716,14715,14714,14713,14712,14711,14710,14709,14708,14707,14706,14705,14704,14703,14702,14701,14700,14699,14698,14696,14695,14694,14693,14692,14691,14690,14689,14688,14687,14686,14685,14684,14683,14682,14681,14680,14679,14678,14677,14676,14675,14674,14673,14672,14671,14670,14669,14668,14667,14666,14665,14664,14663,14662,14661,14660,14659,14658,14657,14656,14655,14654,14653,14652,14651,14650,14649,14648,14647,14646,14645,14644,14643,14642,14641,14640,14639,14638,14637,14636,14635,14634,14633,14632,14631,14630,14629,14628,14627,14626,14625,14624,14623,14622,14621,14620,14619,14618,14617,14616,14615,14614,14613,14612,14611,14610,14609,14608,14607,14606,14605,14604,14603,14602,14601,14600,14599,14598,14597,14596,14595,14594,14593,14592,14591,14590,14589,14588,14587,14586,14585,14584,14583,14582,14581,14580,14579,14578,14577,14576,14575,14574,14573,14572,14571,14570,14569,14568,14567,14566,14565,14564,14563,14562,14561,14560,14559,14558,14557,14556,14555,14554,14553,14552,14551,14550,14549,14548,14547,14546,14545,14544,14543,14542,14541,14540,14539,14538,14537,14536,14535,14534,14533,14532,14531,14530,14529,14528,14527,14526,14525,14524,14523,14522,14521,14520,14519,14518,14517,14516,14515,14514,14513,14512,14511,14510,14509,14508,14507,14506,14505,14504,14503,14502,14501,14500,14499,14498,14497,14496,14495,14494,14493,14492,14491,14490,14489,14488,14487,14486,14485,14484,14483,14482,14481,14480,14479,14478,14477,14476,14475,14474,14473,14472,14471,14470,14469,14468,14467,14466,14465,14464,14463,14462,14461,14460,14459,14458,14457,14456,14455,14454,14453,14452,14451,14450,14449,14448,14447,14446,14445,14444,14443,14442,14441,14440,14439,14438,14437,14436,14435,14434,14433,14432,14431,14430,14429,14428,14427,14426,14425,14424,14423,14422,14421,14420,14419,14418,14417,14416,14415,14414,14413,14412,14411,14410,14409,14408,14407,14406,14405,14404,14403,14402,14401,14400,14399,14398,14397,14396,14395,14394,14393,14392,14391,14390,14389,14388,14387,14386,14385,14384,14383,14382,14381,14380,14379,14378,14377,14376,14375,14374,14373,14372,14371,14370,14369,14368,14367,14366,14365,14364,14363,14362,14361,14360,14359,14358,14357,14356,14355,14354,14353,14352,14351,14350,14349,14348,14347,14346,14345,14344,14343,14342,14341,14340,14339,14337,14336,14335,14334,14333,14332,14331,14330,14329,14328,14327,14326,14325,14324,14323,14322,14321,14320,14319,14318,14317,14316,14315,14314,14313,14312,14311,14310,14309,14308,14307,14306,14305,14304,14303,14302,14301,14300,14299,14298,14297,14296,14295,14294,14293,14292,14291,14290,14289,14288,14287,14286,14285,14284,14283,14282,14281,14280,14279,14278,14277,14276,14275,14273,14272,14271,14270,14269,14268,14267,14266,14265,14264,14263,14262,14261,14260,14259,14258,14257,14256,14255,14254,14253,14252,14251,14249,14248,14247,14246,14245,14244,14243,14242,14241,14240,14239,14238,14237,14236,14235,14234,14233,14231,14229,14228,14227,14226,14225,14224,14223,14222,15620,15619,15618,15617,15616,15615,15614,15613,15612,15611,15610,15609,15608,15607,15606,15605,15604,15603,15602,15601,15600,15599,15598,15597,15596,15595,15594,15593,15592,15591,15590,15589,15588,15587,15586,15585,15584,15583,15582,15581,15580,15579,15578,15577,15576,15575,15574,15573,15572,15571,15570,15569,15568,15567,15566,15565,15564,15563,15562,15561,15560,15559,15558,15557,15556,15555,15554,15553,15552,15551,15550,15549,15548,15547,15546,15545,15544,15543,15542,15541,15540,15539,15538,15537,15536,15535,15534,15533,15532,15531,15530,15529,15528,15527,15526,15525,15524,15523,15522,15521,15520,15519,15518,15517,15516,15515,15514,15513,15512,15511,15510,15509,15508,15507,15506,15505,15504,15503,15502,15501,15500,15499,15498,15497,15496,15495,15494,15493,15492,15491,15490,15489,15488,15487,15486,15485,15484,15483,15482,15481,15480,15479,15478,15477,15476,15475,15474,15473,15472,15471,15470,15469,15468,15467,15466,15465,15464,15463,15462,15461,15460,15459,15458,15457,15456,15455,15454,15453,15452,15451,15450,15449,15448,15447,15446,15445,15444,15443,15442,15441,15440,15439,15438,15437,15436,15435,15434,15433,15432,15431,15430,15429,15428,15427,15426,15425,15424,15423,15422,15421,15420,15419,15418,15417,15416,15415,15414,15413,15412,15411,15410,15409,15408,15407,15406,15405,15404,15403,15402,15401,15400,15399,15398,15397,15396,15395,15394,15393,15392,15391,15390,15389,15388,15387,15386,15385,15384,15383,15382,15381,15380,15379,15378,15377,15376,15375,15374,15373,15372,15371,15370,15369,15368,15367,15366,15365,15364,15363,15362,15361,15360,15359,15358,15357,15356,15355,15354,15353,15352,15351,15350,15349,15348,15347,15346,15345,15344,15343,15342,15341,15340,15339,15338,15337,15336,15335,15334,15333,15332,15331,15330,15329,15328,15327,15326,15325,15324,15323,15322,15321,15320,15319,15318,15317,15316,15315,15314,15313,15312,15311,15310,15309,15308,15307,15306,15305,15304,15303,15302,15301,15300,15299,15298,15297,15296,15295,15294,15293,15292,15291,15290,15289,15288,15287,15286,15285,15284,15283,15282,15281,15280,15279,15278,15277,15276,15275,15274,15273,15272,15271,15270,15269,15268,15267,15266,15265,15264,15263,15262,15261,15260,15259,15258,15257,15256,15255,15254,15253,15252,15251,15250,15249,15248,15247,15246,15245,15244,15243,15242,15241,15240,15239,15238,15237,15236,15235,15234,15233,15232,15231,15230,15229,15228,15227,15226,15225,15224,15223,15222,15221,15220,15219,15218,15217,15216,15215,15214,15213,15212,15211,15210,15209,15208,15207,15206,15205,15204,15203,15202,15201,15200,15199,15198,15197,15196,15195,15194,15193,15192,15191,15190,15189,15188,15187,15186,15185,15184,15183,15182,15181,15180,15179,15178,15177,15176,15175,15174,15173,15172,15171,15170,15169,15168,15167,15166,15165,15164,15163,15162,15161,15160,15159,15158,15157,15156,15155,15154,15153,15152,15151,15150,15149,15148,15147,15146,15145,15144,15143,15142,15141,15140,15139,15138,15137,15136,15135,15134,15133,15132,15131,15130,15129,15128,15127,15126,15125,15124,15123,15122,15121,15120,15119,15118,15117,15116,15115,15114,15113,15112,15111,15110,15109,15108,15107,15106,15105,15104,15103,15102,15101,15100,15099,15098,15097,15096,15095,15094,15093,15092,15091,15090,15089,15088,15087,15086,15085,15084,15083,15082,15081,15080,15079,15078,15077,15076,15075,15074,15073,15072,15071,15070,15069,15068,15067,15066,15065,15064,15063,15062,15061,15060,15059,15058,15057,15056,15055,15054,15053,15052,15051,15050,15049,15048,15047,15046,15045,15044,15043,15042,15041,15040,15039,15038,15037,15036,15035,15032,15031,15030,15028,15027,15026,15025,15024,15023,15022,15021,15020,15019,15018,15017,15016,15015,15014,15013,15012,15011,15010,15009,15008,15007,15006,15005,15004,15003,15002,15001,15000,14999,14998,14997,14996,14995,14994,14993,14992,14991,14990,14989,14988,14987,14986,14985,14984,14983,14982,14981,14980,14979,14978,14977,14976,14975,14974,14973,14972,14971,14970,14969,14968,14967,14966,14965,14964,14963,14962,14961,14960,14959,14958,14957,14956,14955,14954,14953,14952,14951,14950,14949,14948,14947,14946,14945,14944,14943,14942,14941,14940,14939,14938,14937,14936,14935,14934,14933,14932,14931,14930,14929,14928,14927,14926,14925,14924,14923,14922,14921,14920,14919,14918,14917,14916,14915,14914,14913,14912,14911,14910,14909,14908,14907,14906,14905,14904,14903,14902,14901,14900,14899,14898,14897,14896,14895,14894,14893,14892,14891,14890,14889,14888,14887,14886,14885,14884,14883,14882,14881,14880,14879,14878,14877,14876,14875,14874,14873,14872,14871,14870,14869,14868,14867,14866,14865,14864,14863,14862,14861,14860,14859,14858,14857,14856,14855,14854,14853,14852,14851,14850,14849,14848,14847,14846,14845,14844,16168,16167,16166,16165,16164,16163,16162,16161,16160,16159,16158,16157,16156,16155,16154,16153,16152,16151,16150,16149,16148,16147,16146,16145,16144,16143,16142,16141,16140,16139,16138,16137,16136,16135,16134,16133,16132,16131,16130,16129,16128,16127,16126,16125,16124,16123,16122,16121,16120,16119,16118,16117,16116,16115,16114,16113,16112,16111,16110,16109,16108,16107,16106,16105,16104,16103,16102,16101,16100,16099,16098,16095,16094,16093,16092,16091,16090,16089,16088,16087,16086,16085,16084,16083,16082,16081,16080,16079,16077,16075,16074,16073,16072,16071,16070,16069,16068,16067,16066,16065,16064,16063,16062,16061,16060,16059,16058,16057,16056,16055,16054,16053,16052,16051,16050,16049,16048,16047,16046,16045,16044,16043,16042,16041,16040,16039,16038,16037,16036,16035,16034,16033,16032,16031,16030,16029,16028,16027,16026,16025,16024,16023,16022,16021,16020,16019,16018,16017,16016,16015,16014,16013,16012,16011,16010,16009,16008,16007,16006,16005,16004,16003,16002,16001,16000,15999,15998,15997,15996,15995,15994,15993,15992,15991,15990,15989,15988,15987,15986,15985,15984,15982,15981,15980,15979,15978,15977,15976,15975,15974,15973,15972,15971,15970,15969,15968,15967,15966,15965,15964,15963,15962,15961,15960,15959,15958,15957,15956,15955,15954,15953,15952,15951,15950,15949,15948,15947,15946,15945,15944,15943,15942,15941,15940,15939,15938,15937,15936,15935,15934,15933,15932,15931,15930,15929,15928,15927,15926,15925,15924,15923,15922,15921,15920,15919,15918,15917,15916,15915,15914,15913,15912,15911,15910,15909,15908,15907,15906,15905,15904,15903,15902,15901,15900,15899,15898,15897,15896,15895,15894,15893,15892,15891,15890,15889,15888,15887,15886,15885,15884,15883,15882,15881,15880,15879,15878,15877,15876,15875,15874,15873,15872,15871,15870,15869,15868,15867,15866,15865,15864,15863,15862,15861,15860,15859,15858,15857,15856,15855,15854,15853,15852,15851,15850,15849,15848,15847,15846,15845,15844,15843,15842,15841,15840,15839,15838,15837,15836,15835,15834,15833,15832,15831,15830,15829,15828,15827,15826,15825,15824,15823,15822,15821,15820,15819,15818,15817,15816,15815,15814,15813,15812,15811,15810,15809,15808,15807,15806,15805,15804,15803,15802,15801,15800,15799,15798,15797,15796,15795,15794,15793,15792,15791,15790,15789,15788,15787,15786,15785,15784,15783,15782,15781,15780,15779,15778,15777,15776,15775,15774,15773,15772,15771,15770,15769,15768,15767,15766,15765,15764,15763,15762,15761,15760,15759,15758,15757,15756,15755,15754,15753,15752,15751,15750,15749,15748,15747,15746,15745,15744,15743,15742,15741,15740,15739,15738,15737,15736,15735,15734,15733,15732,15731,15730,15729,15728,15727,15726,15725,15724,15723,15722,15721,15720,15719,15718,15717,15716,15715,15714,15713,15712,15711,15710,15709,15707,15706,15705,15704,15703,15702,15701,15700,15699,15698,15697,15696,15695,15694,15693,15692,15691,15690,15689,15688,15687,15686,15685,15684,15683,15682,15681,15680,15679,15678,15677,15676,15675,15674,15673,15672,15671,15670,15669,15668,15667,15666,15665,15664,15663,15662,15661,15660,15659,15658,15657,15656,15655,15654,15653,15652,15651,15650,15649,15648,15647,15646,15645,15644,15643,15642,15641,15640,15639,15638,15637,15636,15635,15634,15633,15632,15631,15630,15629,15628,15627,15626,15625,15624,15623,15622,15621,192,191,190,189,188,187,186,185,184,183,182,181,180,179,178,177,176,175,174,173,172,171,170,169,168,167,166,165,164,163,162,161,160,158,157,156,155,153,152,151,150,149,148,147,146,145,144,143,142,141,140,139,138,137,136,135,134,133,132,131,130,129,128,127,126,125";
                    List<string> issueslists = new List<string>();
                    issueslists = issuedIDs.ToString().Split(',').ToList();

                    #region Issues 

                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsRoslyn_02112019.xml");
                    XmlNode rootNode = xmlDoc["IssueDetails"];

                    foreach (var item in issueslists)
                    {
                        var task = GetIssue(owner, name, Convert.ToInt32(item));

                        if (!task.IsCompleted)
                        {
                            var response = await task.ConfigureAwait(false);
                            Issue issue = response as Issue;

                            if (issue != null && (issue.Labels?.Count() > 0 || issue.Assignee != null))
                            {
                                XmlNode IssueNode = xmlDoc.CreateElement("IssueDetail");
                                XmlElement elemIssueLabelID = xmlDoc.CreateElement("IssueLabelID");
                                elemIssueLabelID.InnerText = issue.Number.ToString();
                                IssueNode.AppendChild(elemIssueLabelID);

                                if (issue.Title != null)
                                {
                                    XmlElement elemLabel = xmlDoc.CreateElement("Title");
                                    elemLabel.InnerText = issue.Title.ToString().Replace(",", "");
                                    IssueNode.AppendChild(elemLabel);
                                    rootNode.AppendChild(IssueNode);
                                }

                                if (issue.Body != null)
                                {
                                    XmlElement elemDescription = xmlDoc.CreateElement("Description");
                                    elemDescription.InnerText = issue.Body.ToString().Replace(",", "");
                                    IssueNode.AppendChild(elemDescription);
                                    rootNode.AppendChild(IssueNode);
                                }

                                if (issue.Title != null && issue.Body != null)
                                {
                                    XmlElement elemDescription = xmlDoc.CreateElement("Title_Description");
                                    elemDescription.InnerText = issue.Title.ToString() + " " + issue.Body.ToString().Replace(",", "");
                                    IssueNode.AppendChild(elemDescription);
                                    rootNode.AppendChild(IssueNode);
                                }

                                if (issue.Labels != null)
                                {
                                    XmlElement elemLabel = xmlDoc.CreateElement("Label");
                                    //elemLabel.InnerText = String.Join("|", issue.Labels.Select(x => x.Name).ToList());
                                    elemLabel.InnerText = issue.Labels?.Select(x => x.Name).ToList().FirstOrDefault();
                                    IssueNode.AppendChild(elemLabel);
                                    rootNode.AppendChild(IssueNode);
                                }
                                if (issue.Assignee != null)
                                {
                                    XmlElement elemAssignee = xmlDoc.CreateElement("Assignee");
                                    elemAssignee.InnerText = issue.Assignee.Login.ToString();
                                    IssueNode.AppendChild(elemAssignee);
                                    rootNode.AppendChild(IssueNode);
                                }
                                else
                                {
                                    //Exclude Issue Creator, Get Collaborators
                                    if (collaborators != null)
                                    {
                                        var results = GetIssueEvents(owner, name, Convert.ToInt32(item));

                                        if (!results.IsCompleted)
                                        {
                                            var potentialAssigneeresponse = await results.ConfigureAwait(false);
                                            var issueevents = potentialAssigneeresponse as IReadOnlyList<Octokit.EventInfo>;
                                            if (issueevents != null)
                                            {
                                                var potentialAssignee = issueevents?.Where(x => collaborators.Contains(x.Actor.Login.ToString())).Select(x=> x.Actor.Login.ToString()).Distinct().FirstOrDefault();
                                                if (potentialAssignee != null)
                                                {
                                                    XmlElement elemAssignee = xmlDoc.CreateElement("Assignee");
                                                    elemAssignee.InnerText = potentialAssignee;
                                                    IssueNode.AppendChild(elemAssignee);
                                                    rootNode.AppendChild(IssueNode);
                                                }
                                            }
                                        }
                                    }

                                    if (issue.CreatedAt != null)
                                    {
                                        XmlElement elemCreatedAt = xmlDoc.CreateElement("CreatedAt");
                                        elemCreatedAt.InnerText = issue.CreatedAt.ToString();
                                        IssueNode.AppendChild(elemCreatedAt);
                                        rootNode.AppendChild(IssueNode);
                                    }

                                    if (issue.ClosedAt != null)
                                    {
                                        XmlElement elemClosedAt = xmlDoc.CreateElement("ClosedAt");
                                        elemClosedAt.InnerText = issue.ClosedAt.ToString();
                                        IssueNode.AppendChild(elemClosedAt);
                                        rootNode.AppendChild(IssueNode);
                                    }
                                }

                            }
                        }

                    }

                    xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsRoslyn_02112019.xml");
                    MessageBox.Show("Done");
                    #endregion Issues
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }


        }

        public async Task<object> GetIssue(string owner, string name, int item)
        {
            try
            {
                return await client.Issue.Get(owner, name, item);

            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async Task<object> GetIssueEvents(string owner, string name, int item)
        {
            try
            {
                return await client.Issue.Events.GetAllForIssue(owner, name, item);

            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async Task<object> GetCollaborators(string owner, string name)
        {
            try
            {
                return await client.Repository.Collaborator.GetAll(owner, name);

            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public async Task<object> GetContributors(string owner, string name)
        {
            try
            {
                return await client.Repository.GetAllContributors(owner, name);

            }
            catch (Exception ex)
            {
                return null;
            }
        }
        #endregion // Private methods

        #region Private enums
        private enum BackgroundType
        {
            None,
            Search,
            Detail
        }
        #endregion // Private enums
    }
}
