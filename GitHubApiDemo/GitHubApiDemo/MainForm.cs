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

                    string issuedIDs = "27307,27306,27305,27304,27303,27301,27300,27299,27298,27297,27296,27295,27294,27293,27292,27291,27290,27289,27288,27287,27286,27285,27284,27283,27282,27281,27280,27279,27278,27277,27276,27275,27274,27273,27272,27271,27270,27269,27268,27267,27266,27265,27264,27263,27262,27261,27260,27259,27258,27257,27256,27255,27254,27253,27252,27251,27250,27249,27248,27247,27246,27245,27244,27243,27242,27241,27240,27239,27238,27237,27236,27235,27234,27233,27232,27231,27230,27229,27228,27227,27226,27225,27224,27223,27222,27221,27220,27219,27218,27217,27216,27215,27214,27213,27212,27211,27210,27209,27208,27207,27206,27205,27204,27203,27202,27201,27200,27199,27198,27197,27196,27195,27194,27193,27192,27191,27190,27189,27188,27187,27186,27185,27184,27183,27182,27181,27180,27179,27178,27177,27176,27175,27174,27173,27172,27171,27170,27169,27168,27167,27166,27165,27164,27163,27162,27161,27160,27159,27158,27157,27156,27155,27154,27153,27152,27151,27150,27149,27148,27147,27146,27145,27144,27143,27142,27141,27140,27139,27138,27137,27136,27135,27134,27133,27132,27131,27130,27129,27128,27127,27126,27125,27124,27123,27122,27121,27120,27119,27118,27117,27116,27115,27114,27113,27112,27111,27110,27109,27108,27107,27106,27105,27104,27103,27102,27101,27100,27099,27098,27097,27096,27095,27094,27093,27092,27091,27090,27089,27088,27087,27086,27085,27084,27083,27082,27081,27080,27079,27078,27077,27076,27075,27074,27073,27072,27071,27070,27069,27068,27067,27066,27065,27064,27063,27062,27061,27060,27059,27058,27057,27056,27055,27054,27053,27052,27051,27050,27049,27048,27047,27046,27045,27044,27043,27042,27041,27040,27039,27038,27037,27036,27035,27034,27033,27032,27031,27030,27029,27028,27027,27026,27025,27024,27023,27022,27021,27020,27019,27018,27017,27016,27015,27014,27013,27012,27011,27010,27009,27008,27007,27006,27005,27004,27003,27002,27001,27000,26999,26997,26996,26995,26994,26993,26992,26991,26990,26989,26988,26987,26986,26985,26984,26983,26982,26981,26980,26979,26978,26977,26976,26975,26974,26973,26972,26971,26970,26969,26968,26967,26966,26965,26964,26963,26962,26961,26960,26959,26958,26957,26956,26955,26954,26953,26952,26951,26950,26949,26948,26947,26946,26945,26944,26943,26942,26941,26940,26939,26938,26937,26936,26935,26934,26933,26932,26931,26930,26929,26928,26927,26926,26925,26924,26923,26922,26921,26920,26919,26918,26917,26916,26915,26914,26913,26912,26911,26910,26909,26908,26907,26906,26905,26904,26903,26902,26901,26900,26899,26898,26897,26896,26895,26894,26893,26892,26891,26890,26889,26888,26887,26886,26885,26884,26883,26882,26881,26880,26879,26878,26877,26876,26875,26874,26873,26872,26871,26870,26869,26868,26867,26866,26865,26864,26863,26862,26861,26860,26859,26858,26857,26856,26855,26854,26853,26852,26851,26850,26849,26848,26847,26846,26845,26844,26843,26842,26841,26840,26839,26838,26837,26836,26835,26834,26833,26832,26831,26830,26829,26828,26827,26826,26825,26824,26823,26822,26821,26820,26819,26818,26817,26816,26815,26814,26813,26812,26811,26810,26809,26808,26807,26806,26805,26804,26803,26802,26801,26800,26799,26798,26797,26796,26795,26794,26793,26792,26791,26790,26789,26788,26787,26786,26785,26784,26783,26782,26781,26780,26779,26778,26777,26776,26775,26774,26773,26772,26771,26770,26769,26768,26767,26766,26764,26763,26762,26761,26760,26759,26758,26757,26756,26755,26754,26753,26752,26751,26750,26749,26748,26747,26746,26745,26744,26743,26742,26741,26740,26739,26738,26737,26736,26735,26734,26733,26732,26731,26730,26729,26728,26727,26726,26725,26724,26723,26722,26721,26720,26719,26718,26717,26716,26715,26714,26713,26712,26711,26710,26709,26708,26707,26706,26705,26704,26703,26702,26701,26700,26699,26698,26697,26696,26695,26694,26693,26692,26691,26690,26689,26688,26687,26686,26685,26684,26683,26682,26681,26680,26679,26678,26677,26676,26675,26674,26673,26672,26671,26670,26669,26668,26667,26665,26664,26663,26662,26661,26660,26659,26658,26657,26656,26655,26654,26653,26652,26651,26650,26649,26648,26647,26646,26645,26644,26643,26642,26641,26640,26639,26638,26637,26636,26635,26634,26633,26632,26631,26630,26629,26628,26627,26626,26625,26624,26623,26622,26621,26620,26619,26618,26617,26616,26615,26614,26613,26612,26611,26610,26609,26608,26607,26606,26605,26604,26603,26602,26601,26600,26599,26598,26597,26596,26595,26594,26593,26592,26591,26590,26589,26588,26587,26586,26585,26584,26583,26582,26581,26580,26579,26578,26577,26576,26575,26574,26572,26571,26570,26569,26568,26567,26566,26565,26564,26563,26562,26561,26560,26559,26558,26557,26556,26555,26554,26553,26552,26551,26550,26549,26548,26547,26546,26545,26544,26543,26542,26541,26540,26539,26538,26537,26536,26535,26534,26533,26532,26531,26530,26529,26528,26527,26526,26525,26524,26523,26522,26521,26520,26519,26518,26517,26516,26515,26514,26513,26512,26511,26510,26509,26508,26507,26506,26505,26504,26503,26502,26501,26500,26499,26498,26497,26496,26495,26494,26493,26492,26491,26490,26489,26488,26487,26486,26485,26484,26483,26482,26481,26480,26479,26478,26477,26476,26475,26474,26473,26472,26471,26470,26469,26468,26467,26466,26465,26464,26463,26462,26461,26460,26459,26458,26457,26456,26455,26454,26453,26452,26451,26450,26449,26448,26446,26445,26444,26443,26442,26441,26440,26439,26438,26437,26436,26435,26434,26433,26432,26431,26430,26429,26428,26427,26426,26425,26424,26423,26422,26421,26420,26419,26418,26417,26416,26415,26414,26413,26412,26411,26410,26409,26408,26407,26406,26405,26404,26403,26402,26401,26400,26399,26398,26397,26396,26395,26394,26393,26392,26391,26390,26389,26388,26387,26386,26385,26384,26383,26382,26381,26380,26379,26378,26377,26376,26375,26374,26373,26372,26371,26370,26369,26368,26367,26366,26365,26364,26363,26362,26361,26360,26359,26358,26357,26356,26355,26354,26353,26352,26351,26350,26349,26348,26347,26346,26345,26344,26343,26342,26341,26340,26339,26338,26337,26336,26335,26334,26333,26332,26331,26330,26329,26328,26327,26326,26325,26324,26323,26322,26321,26320,26319,26318,26317,26316,26315,26314,26313,26312,26311,26310,26309,26308,26307,26306,26305,26304,26303,26302,26301,26300,26299,26298,26297,26296,26295,26294,26293,26292,26291,26290,26289,26288,26287,26286,26285,26284,26283,26282,26281,26280,26279,26278,26277,26276,26275,26274,26273,26272,26271,26270,26269,26268,26267,26266,26265,26264,26263,26262,26261,26260,26259,26258,26257,26256,26255,26254,26253,26252,26251,26250,26249,26248,26247,26246,26245,26244,26243,26242,26241,26240,26239,26238,26237,26236,26235,26234,26233,26232,26231,26230,26229,26228,26227,26226,26225,26224,26223,26222,26221,26220,26219,26218,26217,26216,26215,26214,26213,26212,26211,26210,26209,26208,26207,26206,26205,26204,26203,26202,26201,26200,26199,26198,26196,26195,26194,26193,26192,26191,26190,26189,26188,26187,26186,26185,26184,26183,26182,26181,26180,26179,26178,26177,26176,26175,26174,26173,26172,26171,26170,26169,26168,26167,26166,26165,26164,26163,26162,26161,26160,26159,26158,26157,26156,26155,26154,26153,26152,26151,26150,26149,26148,26147,26146,26145,26144,26143,26142,26141,26140,26139,26138,26137,26136,26135,26134,26133,26132,26131,26130,26129,26128,26127,26126,26125,26124,26123,26122,26121,26120,26119,26118,26117,26116,26115,26114,26113,26112,26111,26110,26109,26108,26107,26106,26105,26104,26103,26102,26101,26100,26099,26098,26097,26096,26095,26094,26093,26092,26091,26090,26089,26088,26087,26086,26085,26084,26083,26082,26081,26080,26079,26078,26077,26076,26075,26074,26073,26072,26071,26070,26069,26068,26067,26066,26065,26064,26063,26062,26061,26060,26059,26058,26057,26056,26055,26054,26053,26052,26051,26050,26049,26048,26047,26046,26045,26044,26043,26042,26041,26040,26039,26038,26037,26036,26035,26034,26033,26032,26031,26030,26029,26028,26027,26026,26025,26024,26023,26022,26021,26020,26019,26018,26017,26016,26015,26014,26013,26012,26011,26010,26009,26008,26007,26006,26004,26003,26002,26001,26000,25999,25998,25997,25996,25995,25994,25993,25992,25991,25990,25989,25988,25987,25986,25985,25984,25983,25982,25981,25980,25979,25978,25977,25976,25975,25974,25973,25972,25971,25970,25969,25968,25967,25966,25965,25964,25963,25962,25961,25960,25959,25958,25957,25956,25955,25954,25953,25952,25951,25950,25949,25948,25947,25946,25945,25944,25943,25942,25941,25940,25939,25938,25937,25936,25935,25934,25933,25932,25931,25930,25929,25928,25927,25926,25925,25924,25923,25922,25921,25920,25919,25918,25917,25916,25915,25914,25913,25912,25911,25910,25909,25908,25907,25906,25905,25904,25903,25902,25901,25900,25899,25898,25897,25896,25895,25894,25893,25892,25891,25890,25889,25888,25887,25886,25885,25884,25883,25882,25881,25880,25879,25878,25877,25876,25875,25874,25873,25872,25871,25870,25869,25868,25867,25866,25865,25864,25863,25862,25861,25860,25859,25858,25857,25856,25855,25854,25853,25852,25851,25850,25849,25848,25847,25846,25845,25844,25843,25842,25841,25840,25839,25838,25837,25836,25835,25834,25833,25832,25831,25830,25829,25828,25827,25826,25825,25824,25823,25822,25821,25820,25819,25818,25817,25816,25815,25814,25813,25812,25811,25810,25809,25808,25807,25806,25805,25804,25803,25802,25801,25800,25799,25798,25797,25796,25795,25794,25793,25792,25791,25790,25789,25788,25787,25786,25785,25784,25783,25782,25781,25780,25779,25778,25777,25776,25775,25774,25773,25772,25771,25770,25769,25768,25767,25766,25765,25764,25763,25762,25761,25760,25759,25758,25757,25756,25755,25754,25753,25752,25751,25750,25749,25748,25747,25746,25745,25744,25743,25741,25740,25739,25738,25737,25736,25735,25734,25733,25732,25731,25730,25729,25728,25727,25726,25725,25724,25723,25722,25721,25720,25719,25718,25717,25716,25715,25714,25713,25712,25711,25710,25709,25708,25707,25706,25705,25704,25703,25702,25701,25700,25699,25698,25697,25696,25695,25694,25693,25692,25691,25690,25689,25688,25687,25686,25685,25684,25683,25682,25681,25680,25679,25678,25677,25676,25675,25674,25673,25672,25671,25670,25669,25668,25667,25666,25665,25664,25663,25662,25661,25660,25659,25658,25657,25656,25655,25654,25653,25652,25651,25650,25649,25648,25647,25646,25645,25644,25643,25642,25641,25640,25639,25638,25637,25636,25635,25634,25633,25632,25631,25630,25629,25628,25627,25626,25625,25624,25623,25622,25621,25620,25619,25618,25617,25616,25615,25614,25613,25612,25611,25610,25609,25608,25607,25606,25605,25604,25603,25602,25601,25600,25599,25598,25597,25596,25595,25594,25593,25592,25591,25590,25589,25588,25587,25586,25585,25584,25583,25582,25581,25580,25579,25578,25577,25576,25575,25574,25573,25572,25571,25570,25569,25568,25567,25566,25565,25564,25563,25562,25561,25560,25559,25558,25557,25556,25555,25554,25553,25552,25551,25550,25549,25548,25547,25546,25545,25544,25543,25542,25541,25540,25539,25538,25537,25536,25535,25534,25533,25532,25531,25530,25529,25528,25527,25526,25525,25523,25522,25521,25520,25519,25518,25517,25516,25515,25514,25513,25512,25511,25510,25509,25508,25507,25506,25505,25504,25503,25502,25501,25500,25499,25498,25497,25496,25495,25494,25493,25492,25491,25490,25489,25488,25487,25486,25485,25484,25483,25482,25481,25480,25479,25478,25477,25476,25475,25474,25473,25472,25471,25470,25469,25468,25467,25466,25465,25464,25463,25462,25461,25460,25459,25458,25457,25456,25455,25454,25453,25452,25451,25450,25449,25448,25447,25446,25445,25444,25443,25442,25441,25440,25439,25438,25437,25436,25435,25434,25433,25432,25431,25430,25429,25428,25427,25426,25425,25424,25423,25422,25421,25420,25419,25418,25417,25416,25415,25414,25413,25412,25411,25410,25409,25408,25407,25406,25405,25404,25403,25402,25401,25400,25399,25398,25397,25396,25395,25394,25393,25392,25391,25390,25389,25388,25387,25386,25385,25384,25383,25382,25381,25380,25379,25378,25377,25376,25375,25374,25373,25372,25371,25370,25369,25368,25367,25366,25365,25364,25363,25362,25361,25360,25359,25358,25357,25356,25355,25354,25353,25352,25351,25350,25349,25348,25347,25346,25345,25344,25343,25342,25341,25340,25339,25338,25337,25336,25335,25334,25333,25332,25331,25330,25329,25328,25327,25326,25325,25324,25323,25322,25321,25320,25319,25318,25317,25316,25315,25314,25313,25312,25311,25310,25309,25308,25307,25306,25305,25304,25303,25302,25301,25300,25299,25298,25297,25296,25295,25294,25293,25292,25291,25290,25289,25288,25287,25286,25285,25284,25283,25282,25281,25280,25279,25278,25277,25276,25275,25274,25273,25272,25271,25270,25269,25268,25267,25266,25265,25264,25263,25262,25261,25260,25259,25258,25257,25256,25255,25254,25253,25252,25251,25250,25249,25248,25247,25246,25245,25244,25243,25242,25241,25240,25239,25238,25237,25236,25235,25234,25233,25232,25231,25230,25229,25228,25227,25226,25225,25224,25223,25222,25221,25220,25219,25218,25217,25216,25215,25214,25213,25212,25211,25210,25209,25208,25207,25206,25205,25204,25203,25202,25201,25200,25199,25197,25196,25195,25194,25193,25192,25191,25190,25189,25188,25187,25186,25185,25184,25183,25182,25181,25180,25179,25178,25177,25176,25175,25174,25173,25172,25171,25170,25169,25168,25167,25166,25165,25164,25163,25162,25161,25160,25159,25158,25157,25156,25155,25154,25153,25152,25151,25150,25149,25148,25147,25146,25145,25144,25143,25142,25141,25140,25139,25138,25137,25136,25135,25134,25133,25132,25131,25130,25129,25128,25127,25126,25125,25124,25123,25122,25121,25120,25119,25118,25117,25116,25115,25114,25113,25112,25111,25110,25109,25108,25107,25106,25105,25104,25103,25102,25101,25100,25099,25098,25097,25096,25095,25094,25093,25092,25091,25090,25089,25088,25087,25086,25085,25084,25083,25082,25081,25080,25079,25078,25077,25076,25075,25074,25073,25072,25071,25070,25069,25068,25067,25066,25065,25064,25063,25062,25061,25060,25059,25058,25057,25056,25055,25054,25053,25052,25051,25050,25049,25048,25047,25046,25045,25044,25043,25042,25041,25040,25039,25038,25037,25036,25035,25034,25033,25032,25031,25030,25029,25028,25027,25026,25025,25024,25023,25022,25021,25020,25019,25018,25017,25016,25015,25014,25013,25012,25011,25010,25009,25008,25007,25006,25005,25004,25003,25002,25001,25000,24999,24998,24997,24996,24995,24994,24993,24992,24991,24990,24989,24988,24987,24986,24985,24984,24983,24982,24981,24980,24979,24978,24977,24976,24975,24974,24973,24972,24971,24970,24969,24968,24967,24966,24965,24964,24963,24962,24961,24960,24959,24958,24957,24956,24955,24954,24953,24952,24951,24950,24949,24948,24947,24946,24945,24942,24941,24940,24939,24938,24937,24936,24935,24934,24933,24932,24931,24930,24929,24928,24927,24926,24925,24924,24923,24922,24921,24920,24919,24918,24917,24916,24915,24914,24913,24911,24910,24909,24908,24907,24906,24905,24904,24903,24902,24901,24900,24899,24898,24897,24896,24895,24894,24893,24892,24891,24890,24889,24888,24887,24886,24885,24884,24883,24882,24881,24880,24879,24878,24877,24876,24875,24874,24873,24872,24871,24870,24869,24868,24867,24866,24865,24864,24863,24862,24861,24860,24859,24858,24857,24856,24855,24854,24853,24852,24851,24850,24849,24848,24847,24846,24845,24844,24843,24842,24841,24840,24839,24838,24837,24836,24835,24834,24833,24832,24831,24830,24829,24828,24827,24826,24825,24824,24823,24822,24821,24820,24819,24818,24817,24816,24815,24814,24813,24812,24811,24810,24809,24808,24807,24806,24805,24804,24803,24802,24801,24800,24799,24798,24797,24796,24795,24794,24793,24792,24791,24790,24789,24788,24787,24786,24785,24784,24783,24782,24781,24780,24779,24778,24777,24776,24775,24774,24773,24772,24771,24770,24769,24768,24767,24766,24765,24764,24763,24762,24761,24760,24759,24758,24757,24756,24755,24754,24753,24752,24751,24750,24749,24748,24747,24746,24745,24744,24743,24742,24741,24740,24739,24738,24737,24736,24735,24734,24733,24732,24731,24730,24729,24728,24727,24726,24725,24724,24723,24722,24721,24720,24719,24718,24717,24716,24715,24714,24713,24712,24711,24710,24709,24708,24707,24706,24705,24704,24703,24702,24701,24700,24699,24698,24697,24696,24695,24694,24693,24692,24691,24690,24689,24688,24687,24686,24685,24684,24683,24682,24681,24680,24679,24678,24677,24676,24675,24674,24673,24672,24671,24670,24669,24668,24667,24666,24665,24664,24663,24662,24661,24660,24659,24658,24657,24656,24655,24654,24653,24652,24651,24650,24649,24648,24647,24646,24645,24644,24643,24642,24641,24640,24639,24638,24637,24636,24635,24634,24633,24632,24631,24630,24629,24628,24627,24626,24625,24624,24623,24622,24621,24620,24619,24618,24617,24616,24615,24614,24613,24612,24611,24610,24609,24608,24607,24606,24605,24604,24603,24602,24601,24600,24599,24598,24597,24596,24595,24594,24593,24592,24591,24590,24589,24588,24587,24586,24585,24584,24583,24582,24581,24580,24579,24578,24577,24576,24575,24574,24573,24572,24571,24570,24569,24568,24567,24566,24565,24564,24563,24562,24561,24560,24558,24557,24556,24555,24554,24553,24552,24551,24550,24549,24548,24547,24546,24545,24544,24543,24542,24541,24540,24539,24538,24537,24536,24535,24534,24533,24532,24531,24530,24529,24528,24527,24526,24525,24524,24523,24522,24521,24520,24519,24518,24517,24516,24515,24514,24513,24512,24511,24510,24509,24508,24507,24506,24505,24504,24503,24502,24501,24500,24499,24498,24497,24496,24495,24494,24493,24492,24491,24490,24489,24488,24487,24486,24485,24484,24483,24482,24481,24480,24479,24478,24477,24476,24475,24474,24473,24472,24471,24470,24469,24468,24467,24466,24465,24464,24463,24462,24461,24460,24459,24458,24457,24456,24455,24454,24453,24452,24451,24450,24449,24448,24447,24446,24445,24444,24443,24442,24441,24440,24439,24438,24437,24436,24435,24434,24433,24432,24431,24430,24429,24428,24427,24426,24425,24424,24423,24422,24421,24420,24419,24418,24417,24416,24415,24414,24413,24412,24411,24410,24409,24408,24407,24406,24405,24404,24403,24402,24401,24400,24399,24398,24397,24396,24395,24394,24393,24392,24391,24390,24389,24388,24387,24386,24385,24384,24383,24382,24381,24380,24379,24378,24377,24376,24375,24374,24373,24372,24371,24370,24369,24368,24367,24366,24365,24364,24363,24362,24361,24360,24359,24358,24357,24356,24355,24354,24353,24352,24351,24350,24349,24348,24347,24346,35484,24345,24344,24343,24342,24341,24340,24339,24338,24337,24336,24335,24334,24333,24332,24331,24330,24329,24328,24327,24326,24325,24324,24323,24322,24321,24320,24319,24318,24317,24316,24315,24314,24313,24312,24311,24310,24309,24308,24307,24306,24305,24304,24303,24302,24301,24300,24299,24298,24297,24296,24295,24294,24293,24292,24291,24290,24289,24288,24287,24286,24285,24284,24283,24282,24281,24280,24279,24275,24274,24273,24272,24271,24270,24269,24268,24267,24266,24265,24264,24263,24262,24261,24260,24259,24258,24257,24256,24255,24254,24253,24252,24251,24250,24249,24248,24247,24246,24245,24244,24243,24242,24241,24240,24239,24238,24237,24236,24235,24234,24233,24232,24231,24230,24229,24228,24227,24226,24225,24224,24223,24222,24221,24220,24219,24218,24217,24216,24215,24214,24213,24212,24211,24210,24209,24208,24207,24206,24205,24204,24203,24202,24201,24200,24199,24198,24197,24196,24195,24194,24193,24192,24191,24190,24189,24188,24187,24186,24185,24184,24183,24182,24181,24180,24179,24178,24177,24176,24175,24174,24173,24172,24171,24170,24169,24168,24167,24166,24165,24164,24163,24162,24161,24160,24159,24158,24157,24156,24155,24154,24153,24152,24151,24150,24149,24148,24147,24146,24145,24144,24143,24142,24141,24140,24139,24138,24137,24136,24135,24134,24133,24132,24131,24130,24129,24128,24127,24126,24125,24124,24123,24122,24121,24120,24119,24118,24117,24116,24115,24114,24113,24112,24111,24110,24109,24108,24107,24106,24105,24104,24103,24102,24101,24100,24099,24098,24097,24096,24095,24094,24093,24092,24091,24090,24089,24088,24087,24086,24085,24084,24083,24082,24081,24080,24079,24078,24077,24076,24075,24074,24073,24072,24071,24070,24069,24068,24067,24066,24065,24064,24063,24062,24061,24060,24059,24058,24057,24056,24055,24054,24053,24052,24051,24050,24049,24048,24047,24046,24045,24044,24043,24042,24041,24040,24039,24038,24037,24036,24035,24034,24033,24032,24031,24030,24029,24028,24027,24026,24025,24024,24023,24022,24021,24020,24019,24018,24017,24016,24015,24014,24013,24012,24011,24010,24009,24008,24007,24006,24005,24004,24003,24002,24001,24000,23999,23998,23997,23996,23995,23994,23993,23992,23991,23990,23989,23988,23987,23986,23985,16861,16860,16859,16858,16857,16856,16855,16854,16853,16852,16851,16850,16849,16848,16847,16846,16845,16844,16843,16842,16841,16840,16839,16838,16837,16836,16835,16834,16833,16832,16831,16830,16829,16828,16827,16826,16825,16824,16823,16822,16821,16820,16819,16818,16817,16816,16815,16814,16813,16812,16811,16810,16809,16808,16807,16806,16805,16804,16803,16802,16801,16800,16799,16798,16797,16796,16795,16794,16793,16792,16791,16790,16789,16788,16787,16786,16785,16784,16783,16782,16781,16780,16779,16778,16777,16776,16775,16774,16773,16772,16771,16770,16769,16768,16767,16766,16765,16764,16763,16762,16761,16760,16759,16758,16757,16756,16755,16754,16753,16752,16751,16750,16749,16748,16747,16746,16745,16744,16743,16742,16741,16740,16739,16738,16737,16736,16735,16734,16733,16732,16731,16730,16729,16728,16727,16726,16725,16724,16723,16722,16721,16720,16719,16718,16717,16716,16715,16714,16713,16712,16711,16710,16709,16708,16707,16706,16705,16704,16703,16702,16701,16700,16699,16698,16697,16696,16695,16694,16693,16692,16691,16690,16689,16688,16687,16686,16685,16684,16683,16682,16681,16680,16679,16678,16677,16676,16675,16674,16673,16672,16671,16670,16669,16668,16667,16666,16665,16664,16663,16662,16661,16660,16659,16658,16657,16656,16655,16654,16653,16652,16651,16650,16649,16648,16647,16646,16645,16644,16643,16642,16641,16640,16639,16638,16637,16636,16635,16634,16633,16632,16631,16630,16629,16628,16627,16626,16625,16624,16623,16622,16621,16620,16619,16618,16617,16616,16615,16614,16613,16612,16611,16610,16609,16608,16607,16606,16605,16604,16603,16602,16601,16600,16599,16598,16597,16596,16595,16594,16593,16592,16591,16590,16589,16588,16587,16586,16585,16584,16583,16582,16581,16580,16579,16578,16577,16576,16575,16574,16573,16572,16571,16570,16569,16568,16567,16566,16565,16564,16563,16562,16561,16560,16559,16558,16557,16556,16555,16554,16553,16552,16551,16550,16549,16548,16547,16546,16545,16544,16543,16542,16541,16540,16539,16538,16537,16536,16535,16534,16533,16532,16531,16530,16529,16528,16527,16526,16525,16524,16523,16522,16521,16520,16519,16518,16517,16516,16515,16514,16513,16512,16511,16510,16509,16508,16507,16506,16505,16504,16503,16502,16501,16500,16499,16498,16497,16496,16495,16494,16493,16492,16491,16490,16489,16488,16487,16486,16485,16484,16483,16482,16481,16480,16479,16478,16477,16476,16475,16474,16473,16472,16471,16469,16468,16467,16466,16465,16464,16463,16462,16461,16460,16459,16458,16457,16456,16455,16454,16453,16452,16451,16450,16449,16448,16447,16446,16445,16444,16443,16442,16441,16440,16439,16438,16437,16436,16435,16434,16433,16432,16431,16430,16429,16428,16427,16426,16425,16424,16423,16422,16421,16420,16419,16418,16417,16416,16415,16414,16413,16412,16411,16410,16409,16408,16407,16406,16405,16404,16403,16402,16401,16400,16399,16398,16397,16396,16395,16394,16393,16392,16391,16390,16389,16388,16387,16386,16385,16384,16383,16382,16381,16380,16379,16378,16377,16376,16375,16374,16373,16372,16371,16370,16369,16368,16367,16366,16365,16364,16363,16362,16361,16360,16359,16358,16357,16356,16355,16354,16353,16352,16351,16350,16349,16348,16347,16346,16345,16344,16343,16342,16341,16340,16339,16338,16337,16336,16335,16334,16333,16332,16331,16330,16329,16328,16327,16326,16325,16324,16323,16322,16321,16320,16319,16318,16317,16316,16315,16314,16313,16312,16311,16310,16309,16308,16307,16306,16305,16304,16303,16302,16301,16300,16299,16298,16297,16295,16293,16292,16291,16290,16289,16288,16287,16286,16285,16284,16283,16282,16281,16280,16279,16278,16277,16276,16275,16274,16273,16272,16271,16270,16269,16268,16267,16266,16265,16264,16263,16262,16261,16260,16259,16258,16257,16256,16255,16254,16253,16252,16251,16250,16249,16248,16247,16246,16245,16244,16243,16242,16241,16240,16239,16238,16237,16236,16235,16234,16233,16232,16231,16230,16229,16228,16227,16226,16225,16224,16223,16222,16221,16220,16219,16218,16217,16216,16215,16214,16213,16212,16211,16210,16209,16208,16207,16206,16205,16204,16203,16202,16201,16200,16199,16198,16197,16196,16195,16194,16193,16192,16191,16190,16189,16188,16187,16186,16185,16184,16183,16182,16181,16180,16179,16178,16177,16176,16175,16174,16173,16172,16171,16170,16169,17474,17473,17472,17471,17470,17469,17468,17467,17466,17465,17464,17463,17462,17461,17460,17459,17458,17457,17456,17455,17454,17453,17452,17451,17450,17449,17448,17447,17446,17445,17444,17443,17442,17441,17440,17439,17438,17437,17436,17435,17434,17433,17432,17431,17430,17429,17428,17427,17426,17425,17424,17423,17422,17421,17420,17419,17418,17417,17416,17415,17414,17413,17412,17411,17410,17409,17408,17407,17406,17405,17404,17403,17402,17401,17400,17399,17398,17397,17396,17395,17394,17393,17392,17391,17390,17389,17388,17387,17386,17385,17384,17383,17382,17381,17380,17379,17378,17377,17376,17375,17374,17373,17372,17371,17370,17369,17368,17367,17366,17365,17364,17363,17362,17361,17360,17359,17358,17357,17356,17355,17354,17353,17352,17351,17350,17349,17348,17347,17346,17345,17344,17343,17342,17341,17340,17339,17338,17337,17336,17335,17334,17333,17332,17331,17330,17329,17328,17327,17326,17325,17324,17323,17322,17321,17320,17319,17318,17317,17316,17315,17314,17313,17312,17311,17310,17309,17308,17307,17306,17305,17304,17303,17302,17301,17300,17299,17298,17297,17296,17295,17294,17293,17292,17291,17290,17289,17288,17287,17286,17285,17284,17283,17282,17281,17280,17279,17278,17277,17276,17275,17274,17273,17272,17271,17270,17269,17268,17267,17266,17265,17264,17263,17262,17261,17260,17259,17258,17257,17256,17255,17254,17253,17252,17251,17250,17249,17248,17247,17246,17245,17244,17243,17242,17241,17240,17239,17238,17237,17236,17235,17234,17233,17232,17231,17230,17229,17228,17227,17226,17225,17224,17223,17222,17221,17220,17219,17218,17217,17216,17215,17214,17213,17212,17211,17210,17209,17208,17207,17206,17205,17204,17203,17202,17201,17200,17199,17198,17197,17196,17195,17194,17193,17192,17191,17190,17189,17188,17187,17186,17185,17184,17183,17182,17181,17180,17179,17178,17177,17176,17175,17174,17173,17172,17171,17170,17169,17168,17167,17166,17165,17164,17163,17162,17161,17160,17159,17158,17157,17156,17155,17154,17153,17152,17151,17150,17149,17148,17147,17146,17145,17144,17143,17142,17141,17140,17139,17138,17137,17136,17135,17134,17133,17132,17131,17130,17129,17128,17127,17126,17125,17124,17123,17122,17121,17120,17119,17118,17117,17116,17115,17114,17113,17112,17111,17110,17109,17108,17107,17106,17105,17104,17103,17102,17101,17100,17099,17098,17097,17096,17095,17094,17093,17092,17091,17090,17089,17088,17087,17086,17085,17084,17083,17082,17081,17080,17079,17078,17077,17076,17075,17074,17073,17072,17071,17070,17069,17068,17067,17066,17065,17064,17063,17062,17061,17060,17059,17058,17057,17056,17055,17054,17053,17052,17051,17050,17049,17048,17047,17046,17045,17044,17043,17042,17041,17040,17039,17038,17037,17036,17035,17034,17033,17032,17031,17030,17029,17028,17027,17026,17025,17024,17023,17022,17021,17020,17019,17018,17017,17016,17015,17014,17013,17012,17011,17010,17009,17008,17007,17006,17005,17004,17003,17002,17001,17000,16999,16998,16997,16996,16995,16994,16993,16992,16991,16990,16989,16988,16987,16986,16985,16984,16983,16982,16981,16980,16979,16978,16977,16976,16975,16974,16973,16972,16971,16970,16969,16968,16967,16966,16965,16964,16963,16962,16961,16960,16959,16958,16957,16956,16955,16954,16953,16952,16951,16950,16949,16948,16947,16946,16945,16944,16943,16942,16941,16940,16939,16938,16937,16936,16935,16934,16933,16932,16931,16930,16929,16928,16927,16926,16925,16924,16923,16922,16921,16920,16919,16918,16917,16916,16915,16914,16913,16912,16911,16910,16909,16908,16907,16906,16905,16904,16903,16902,16901,16900,16899,16898,16897,16896,16895,16894,16893,16892,16891,16890,16889,16888,16887,16886,16885,16884,16883,16882,16881,16880,16879,16878,16877,16876,16875,16874,16873,16872,16871,16870,16869,16868,16867,16866,16865,16864,16863,16862,18359,18358,18357,18356,18355,18354,18353,18352,18351,18350,18349,18348,18347,18346,18345,18344,18343,18342,18341,18340,18339,18338,18337,18336,18335,18334,18333,18332,18331,18330,18329,18328,18327,18326,18325,18324,18323,18322,18321,18320,18319,18318,18317,18316,18315,18314,18313,18312,18311,18310,18309,18308,18307,18306,18305,18304,18303,18302,18301,18300,18299,18298,18297,18296,18295,18294,18293,18292,18291,18290,18289,18288,18287,18286,18285,18284,18283,18282,18281,18280,18279,18278,18277,18276,18275,18274,18273,18272,18271,18270,18269,18268,18267,18266,18265,18264,18263,18262,18261,18260,18259,18258,18257,18256,18255,18254,18253,18252,18251,18250,18249,18248,18247,18246,18245,18244,18243,18242,18241,18240,18239,18238,18237,18236,18235,18234,18233,18232,18231,18230,18229,18228,18227,18226,18225,18224,18223,18222,18221,18220,18219,18218,18217,18216,18215,18214,18213,18212,18211,18210,18209,18208,18207,18206,18205,18204,18203,18202,18201,18200,18199,18198,18197,18196,18195,18194,18193,18192,18191,18190,18189,18188,18187,18186,18185,18184,18183,18182,18181,18180,18179,18178,18177,18176,18175,18174,18173,18172,18171,18170,18169,18168,18167,18166,18165,18164,18163,18162,18161,18160,18159,18158,18157,18156,18155,18154,18153,18152,18151,18150,18149,18148,18147,18146,18145,18144,18143,18142,18141,18140,18139,18138,18137,18136,18135,18134,18133,18132,18131,18130,18129,18128,18127,18126,18125,18124,18123,18122,18121,18120,18119,18118,18117,18116,18115,18114,18113,18112,18111,18110,18109,18108,18107,18106,18105,18104,18103,18102,18101,18100,18099,18098,18097,18096,18095,18094,18093,18092,18091,18090,18089,18088,18087,18086,18085,18084,18083,18082,18081,18080,18079,18078,18077,18076,18075,18074,18073,18072,18071,18070,18069,18068,18067,18066,18065,18064,18063,18062,18061,18060,18059,18058,18057,18056,18055,18054,18053,18052,18051,18050,18049,18048,18047,18046,18045,18044,18043,18042,18041,18040,18039,18038,18037,18036,18035,18034,18033,18032,18031,18030,18029,18028,18027,18026,18025,18024,18023,18022,18021,18020,18019,18018,18017,18016,18015,18014,18013,18012,18011,18010,18009,18008,18007,18006,18005,18004,18003,18002,18001,18000,17999,17998,17997,17996,17995,17994,17993,17992,17991,17990,17989,17988,17987,17986,17985,17984,17983,17982,17981,17980,17979,17978,17977,17976,17975,17974,17973,17972,17971,17970,17969";
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
