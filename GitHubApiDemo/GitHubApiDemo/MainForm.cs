using System;
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
using System.Xml.Linq;

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
                        // GetIssueDetails();
                        RemoveCodeFromText();
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
            string name = "corefx";

            // string owner = "SVF-tools";
            //string name = "SVF";
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsCorefx_05112019_2.xml");
            XmlNode rootNode = xmlDoc["IssueDetails"];
            try
            {
                var Contributors = GetContributors(owner, name);
                if (!Contributors.IsCompleted)
                {
                    var collaboratorresponse = await Contributors.ConfigureAwait(false);
                    IReadOnlyList<RepositoryContributor> _collaboratorresponse = collaboratorresponse as IReadOnlyList<RepositoryContributor>;
                    List<string> collaborators = _collaboratorresponse?.Select(x => x.Login).ToList();

                    string issuedIDs = "10763,10762,10761,10760,10759,10758,10757,10756,10755,10754,10753,10752,10751,10750,10749,10748,10747,10746,10745,10744,10743,10742,10741,10740,10739,10738,10737,10736,10735,10734,10733,10732,10731,10730,10729,10728,10727,10726,10725,10724,10723,10722,10721,10720,10719,10718,10717,10716,10715,10714,10713,10712,10711,10710,10709,10708,10707,10706,10705,10704,10703,10702,10701,10700,10699,10698,10697,10696,10695,10694,10693,10692,10691,10690,10689,10688,10687,10686,10685,10684,10683,10682,10681,10680,10679,10678,10677,10676,10675,10674,10673,10672,10671,10670,10669,10668,10667,10666,10665,10664,10663,10662,10661,10660,10659,10658,10657,10656,10655,10654,10653,10652,10651,10650,10649,10648,10647,10646,10645,10644,10643,10642,10641,10640,10639,10638,10637,10636,10635,10634,10633,10632,10631,10630,10629,10628,10627,10626,10625,10624,10623,10622,10621,10620,10619,10618,10617,10616,10615,10614,10613,10612,10611,10610,10609,10608,10607,10606,10605,10604,10603,10602,10601,10600,10599,10598,10597,10596,10595,10594,10593,10592,10591,10590,10589,10588,10587,10586,10585,10584,10583,10582,10581,10580,10579,10578,10577,10576,10575,10574,10573,10572,10571,10570,10569,10568,10567,10566,10565,10564,10563,10562,10561,10560,10559,10558,10557,10556,10555,10554,10553,10552,10551,10550,10549,10548,10547,10546,10545,10544,10543,10542,10541,10540,10539,10538,10537,10536,10535,10534,10533,10532,10531,10530,10529,10528,10527,10526,10525,10524,10523,10522,10521,10520,10519,10518,10517,10516,10515,10514,10513,10512,10511,10510,10509,10508,10507,10506,10505,10504,10503,10502,10501,10500,10499,10498,10497,10496,10495,10494,10493,10492,10491,10490,10489,10488,10487,10486,10485,10484,10483,10482,10481,10480,10479,12263,12262,12261,12260,12259,12258,12257,12256,12255,12254,12253,12252,12251,12250,12249,12248,12247,12246,12245,12244,12243,12242,12241,12240,12239,12238,12237,12236,12235,12234,12233,12232,12231,12230,12229,12228,12227,12226,12225,12224,12223,12222,12221,12220,12219,12218,12217,12216,12215,12214,12213,12212,12211,12210,12209,12208,12207,12206,12205,12204,12203,12202,12201,12200,12199,12198,12197,12196,12195,12194,12193,12192,12191,12190,12189,12188,12187,12186,12185,12184,12183,12182,12181,12180,12179,12178,12177,12176,12175,12174,12173,12172,12171,12170,12169,12168,12167,12166,12165,12164,12163,12162,12161,12160,12159,12158,12157,12156,12155,12154,12153,12152,12151,12150,12149,12148,12147,12146,12145,12144,12143,12142,12141,12140,12139,12138,12137,12136,12135,12134,12133,12132,12131,12130,12129,12128,12127,12126,12125,12124,12123,12122,12121,12120,12119,12118,12117,12116,12115,12114,12113,12112,12111,12110,12109,12108,12107,12106,12105,12104,12103,12102,12101,12100,12099,12098,12097,12096,12095,12094,12093,12092,12091,12090,12089,12088,12087,12086,12085,12084,12083,12082,12081,12080,12079,12078,12077,12076,12075,12074,12073,12071,12070,12069,12068,12067,12066,12065,12064,12063,12062,12061,12060,12059,12058,12057,12056,12055,12054,12053,12052,12051,12050,12049,12048,12047,12046,12045,12044,12043,12042,12041,12040,12039,12038,12037,12036,12035,12034,12033,12032,12031,12030,12029,12028,12027,12026,12025,12024,12023,12022,12021,12020,12019,12018,12017,12016,12015,12014,12013,12012,12011,12010,12009,12008,12007,12006,12005,12004,12003,12002,12001,12000,11999,11998,11997,11996,11995,11994,11993,11992,11991,11990,11989,11988,11987,11986,11985,11984,11983,11982,11981,11980,11979,11978,11977,11976,11975,11974,11973,11972,11971,11970,11969,11968,11967,11966,11965,11964,11963,11962,11961,11960,11959,11958,11957,11956,11955,11954,11953,11952,11951,11950,11949,11948,11947,11946,11945,11944,11943,11942,11941,11940,11939,11938,11937,11936,11935,11934,11933,11932,11931,11930,11929,11928,11927,11926,11925,11924,11923,11922,11921,11920,11919,11918,11917,11916,11915,11914,11913,11912,11911,11910,11909,11908,11907,11906,11905,11904,11903,11902,11901,11900,11899,11898,11897,11896,11895,11894,11893,11892,11891,11890,11889,11888,11887,11886,11885,11884,11883,11882,11881,11880,11879,11878,11877,11876,11875,11874,11873,11872,11871,11870,11869,11868,11867,11866,11865,11864,11863,11862,11861,11860,11859,11858,11857,11856,11855,11854,11853,11852,11851,11850,11849,11848,11847,11846,11845,11844,11843,11842,11841,11840,11839,11838,11837,11836,11835,11834,11833,11832,11831,11830,11829,11828,11827,11826,11825,11824,11823,11822,11821,11820,11819,11818,11817,11816,11815,11814,11813,11812,11811,11810,11809,11808,11807,11806,11805,11804,11803,11802,11801,11800,11799,11798,11797,11796,11795,11794,11793,11792,11791,11790,11789,11788,11787,11786,11785,11784,11783,11782,11781,11780,11779,11778,11777,11776,11775,11774,11773,11772,11771,11770,11769,11768,11767,11766,11765,11764,11763,11762,11761,11760,11759,11758,11757,11755,11754,11753,11752,11751,11750,11749,11748,11747,11746,11745,11744,11743,11742,11741,11740,11739,11738,11737,11736,11735,11734,11733,11732,11731,11730,11729,11728,11727,11726,11725,11724,11723,11722,11721,11720,11719,11718,11717,11716,11715,11714,11713,11712,11711,11710,11709,11708,11707,11706,11705,11704,11703,11702,11701,11700,11699,11698,11697,11696,11695,11694,11693,11692,11691,11690,11689,11688,11687,11686,11685,11684,11683,11682,11681,11680,11679,11678,11677,11676,11675,11674,11673,11672,11671,11670,11669,11668,11667,11666,11665,11664,11663,11662,11661,11660,11659,11658,11657,11656,11655,11654,11653,11652,11651,11650,11649,11648,11647,11646,11645,11644,11643,11642,11641,11640,11639,11638,11637,11636,11635,11634,11633,11632,11631,11630,11629,11628,11627,11626,11625,11624,11623,11622,11621,11620,11619,11618,11617,11616,11615,11614,11613,11612,11611,11610,11609,11608,11607,11606,11605,11604,11603,11602,11601,11600,11599,11598,11597,11596,11595,11594,11593,11592,11591,11590,11589,11588,11587,11586,11585,11584,11583,11582,11581,11580,11579,11578,11577,11576,11575,11574,11573,11572,11571,11570,11569,11568,11567,11566,11565,11564,11563,11562,11561,11560,11559,11558,11557,11556,11555,11554,11553,11552,11551,11550,11549,11548,11547,11546,11545,11544,11543,11542,11541,11540,11539,11538,11537,11536,11535,11534,11533,11532,11531,11530,11529,11528,11527,11526,11525,11524,11523,11522,11521,11520,11519,11518,11517,11516,11515,11514,11513,11512,11511,11510,11509,11508,11507,11506,11505,11504,11503,11502,11501,11500,11499,11498,11497,11496,11495,11494,11493,11492,11491,11490,11489,11488,11487,11486,11485,11484,11483,11482,11481,11480,11479,11478,11477,11476,11475,11474,11473,11472,11471,11470,11469,11468,11467,11466,11465,11464,11463,11462,11461,11460,11459,11458,11457,11456,11455,11454,11453,11452,11451,11450,11449,11448,11447,11446,11445,11444,11443,11442,11441,11440,11439,11438,11437,11436,11435,11434,11433,11432,11431,11430,11429,11428,11427,11426,11425,11424,11423,11422,11421,11420,11419,11418,11417,11416,11415,11414,11413,11412,11411,11410,11409,11408,11407,11406,11405,11404,11403,11402,11401,11400,11399,11398,11397,11396,11395,11394,11393,11392,11391,11390,11389,11388,11387,11386,11385,11384,11383,11382,11381,11380,11379,11378,11377,11376,11375,11374,11373,11372,11371,11370,11369,11368,11367,11366,11365,11364,11363,11362,11361,11360,11359,11358,11357,11356,11355,11354,11353,11352,11351,11350,11349,11348,11347,11346,11345,11344,11343,11342,11341,11340,11339,11338,11337,11336,11335,11334,11333,11332,11331,11330,11329,11328,11327,11326,11325,11324,11323,11322,11321,11320,11319,11318,11317,11316,13165,13164,13163,13162";
                    List<string> issueslists = new List<string>();
                    issueslists = issuedIDs.ToString().Split(',').ToList();

                    #region Issues 
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
                                                var potentialAssignee = issueevents?.Where(x => collaborators.Contains(x.Actor.Login.ToString())).Select(x => x.Actor.Login.ToString()).Distinct().FirstOrDefault();
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

                    xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsCorefx_05112019_2.xml");
                    MessageBox.Show("Done");
                    #endregion Issues
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsCorefx_05112019_2.xml");
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


        public void RemoveCodeFromText()
        {
            string xmlPath = @"C:\PhD\Workbrench\GitHub_NeturalNetworks\Datasets\IssueDetailsRoslyn_02112019_3_RemoveCode.xml";
            System.IO.StreamReader xmlStreamReader =
                new System.IO.StreamReader(xmlPath);
            System.Xml.XmlDocument xmlDoc = new System.Xml.XmlDocument();
           

            xmlDoc.Load(xmlStreamReader);
            xmlStreamReader.Close();
            var rulesDescNodes = xmlDoc.DocumentElement.GetElementsByTagName("IssueDetail");
            if (rulesDescNodes != null)
            {
                foreach (XmlNode node in rulesDescNodes)
                {
                    /*Remove Code from Description*/
                    var _valueD = node.ChildNodes[2].InnerText;

                    int startIndex_D = _valueD.IndexOf("```");
                    int endIndex_D = _valueD.LastIndexOf("```");
                    int length_D = endIndex_D - startIndex_D + 1;

                    if (startIndex_D > -1 && endIndex_D > -1)
                    {
                        _valueD = _valueD.Remove(startIndex_D, length_D);
                        node.ChildNodes[2].InnerText = _valueD;
                    }

                    /*Remove Code From Title_Description*/
                    var _value = node.ChildNodes[3].InnerText;

                    int startIndex = _value.IndexOf("```");
                    int endIndex = _value.LastIndexOf("```");
                    int length = endIndex - startIndex + 1;

                    if (startIndex > -1 && endIndex > -1)
                    {
                        _value = _value.Remove(startIndex, length);
                        node.ChildNodes[3].InnerText = _value;
                    }
                }
                xmlDoc.Save(@"C:\PhD\Workbrench\GitHub_NeturalNetworks\Datasets\IssueDetailsRoslyn_02112019_3_RemoveCode.xml");
            }
            MessageBox.Show("Done!");

           
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
