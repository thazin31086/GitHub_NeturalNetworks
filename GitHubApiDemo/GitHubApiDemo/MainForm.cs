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
            string name = "corefx";

            // string owner = "SVF-tools";
            //string name = "SVF";
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsCorefx_05112019.xml");
            XmlNode rootNode = xmlDoc["IssueDetails"];
            try
            {
                var Contributors = GetContributors(owner, name);
                if (!Contributors.IsCompleted)
                {
                    var collaboratorresponse = await Contributors.ConfigureAwait(false);
                    IReadOnlyList<RepositoryContributor> _collaboratorresponse = collaboratorresponse as IReadOnlyList<RepositoryContributor>;
                    List<string> collaborators = _collaboratorresponse?.Select(x => x.Login).ToList();
                    
                    string issuedIDs = "6769,6768,6767,6766,6765,6764,6763,6762,6761,6760,6759,6758,6757,6756,6755,6754,6753,6752,6751,6750,6749,6748,6747,6746,6745,6744,6743,6742,6741,6740,6739,6738,6737,6736,6735,6734,6733,6732,6731,6730,6729,6728,6727,6726,6725,6724,6723,6722,6721,6720,6719,6718,6717,6716,6715,6714,6713,6712,6711,6710,6709,6708,6707,6706,6705,6704,6703,6702,6701,6700,6699,6698,6697,6696,6695,6694,6693,6692,6691,6690,6689,6688,6687,6686,6685,6684,6683,6682,6681,6680,6679,6678,6677,6676,6675,6674,6673,6672,6671,6670,6669,6668,6666,6665,6664,6663,6662,6661,6660,6659,6656,6655,6654,6653,6651,6650,6649,6648,6647,6646,6645,6644,6643,6642,6641,6640,6639,6638,6637,6636,6635,6634,6633,6632,6631,6630,6629,6628,6627,6626,6625,6624,6623,6622,6621,6620,6619,6618,6617,6616,6615,6614,6613,6612,6611,6610,6609,6608,6607,6606,6605,6604,6603,6602,6601,6600,6599,6598,6597,6596,6595,6594,6593,6592,6591,6590,6589,6588,6587,6586,6585,6584,6583,6582,6581,6580,6579,6578,6577,6576,6575,6574,6573,6572,6571,6570,6569,6568,6567,6566,6565,6564,6563,6562,6561,6560,6559,6558,6557,6556,6555,6554,6553,6552,6551,6550,6549,6548,6547,6546,6545,6544,6543,6542,6541,6540,6539,6538,6537,6536,6535,6534,6533,6532,6531,6530,6529,6528,6527,6526,6525,6524,6523,6522,6521,6520,6519,6518,6517,6516,6515,6514,6513,6512,6511,6510,6509,6508,6507,6506,6504,6503,6502,6501,6500,6499,6498,6497,6496,6495,6494,6493,6492,6491,6490,6489,6488,6487,6486,6485,6484,6483,6482,6481,6480,6479,6478,6477,6476,6475,6474,6473,6472,6471,6470,6469,6468,6467,6466,6465,6464,6463,6462,6461,6460,6459,6458,6457,6456,6455,6454,6453,6452,6451,6450,6449,6448,6447,6446,6445,6444,6443,6442,6441,6440,6439,6438,6437,6436,6435,6434,6433,6432,6431,6430,6429,6428,6427,6426,6425,6424,6423,6422,6421,6420,6419,6418,6417,8204,8203,8202,8201,8200,8199,8198,8197,8196,8195,8194,8193,8192,8191,8190,8189,8188,8187,8186,8185,8184,8183,8182,8181,8180,8179,8178,8177,8176,8175,8174,8173,8172,8171,8170,8169,8168,8167,8166,8165,8164,8163,8162,8161,8160,8159,8158,8157,8156,8155,8154,8153,8152,8151,8150,8149,8148,8147,8146,8145,8144,8143,8142,8141,8140,8139,8138,8137,8136,8135,8134,8133,8132,8131,8129,8128,8127,8126,8125,8124,8123,8122,8121,8120,8119,8118,8117,8116,8115,8114,8113,8112,8111,8110,8109,8108,8107,8106,8105,8104,8103,8102,8101,8100,8099,8098,8097,8096,8095,8094,8093,8092,8091,8090,8089,8088,8087,8086,8085,8084,8083,8082,8081,8080,8079,8078,8077,8076,8075,8074,8073,8072,8071,8070,8069,8068,8067,8066,8065,8064,8063,8062,8061,8060,8059,8058,8057,8056,8055,8054,8053,8052,8051,8050,8049,8047,8046,8045,8044,8043,8042,8041,8040,8039,8038,8037,8036,8035,8034,8033,8032,8031,8030,8029,8028,8027,8026,8025,8024,8023,8022,8021,8020,8019,8018,8017,8016,8015,8014,8013,8012,8011,8010,8009,8008,8007,8006,8005,8004,8003,8002,8001,8000,7999,7998,7997,7996,7995,7994,7993,7992,7991,7990,7989,7988,7987,7986,7985,7984,7983,7982,7981,7980,7979,7978,7977,7976,7975,7974,7972,7971,7970,7969,7968,7967,7966,7965,7964,7963,7962,7961,7960,7959,7958,7957,7956,7955,7954,7953,7952,7951,7950,7949,7948,7947,7946,7945,7944,7943,7942,7941,7940,7939,7938,7937,7936,7935,7934,7933,7932,7931,7930,7929,7928,7927,7926,7925,7924,7923,7922,7921,7920,7919,7918,7917,7916,7915,7914,7913,7912,7911,7910,7909,7908,7907,7906,7905,7904,7903,7902,7901,7900,7899,7898,7897,7896,7895,7894,7893,7892,7891,7890,7889,7888,7887,7886,7885,7884,7883,7882,7881,7880,7879,7878,7877,7876,7875,7874,7873,7872,7871,7870,7869,7868,7867,7866,7865,7864,7863,7862,7861,7860,7859,7858,7857,7856,7855,7854,7853,7852,7851,7850,7849,7848,7847,7846,7845,7844,7843,7842,7841,7840,7839,7838,7837,7836,7835,7834,7833,7832,7831,7829,7828,7827,7826,7825,7824,7823,7822,7821,7820,7819,7818,7817,7816,7815,7814,7813,7812,7811,7810,7809,7808,7807,7806,7805,7804,7803,7802,7801,7800,7799,7798,7797,7796,7795,7794,7793,7792,7791,7790,7789,7788,7787,7786,7785,7784,7783,7782,7781,7780,7779,7778,7777,7776,7775,7774,7773,7772,7771,7770,7769,7768,7767,7766,7765,7764,7763,7762,7761,7760,7759,7758,7757,7756,7754,7753,7752,7751,7750,7749,7748,7747,7746,7745,7744,7743,7742,7741,7740,7739,7738,7737,7736,7735,7734,7733,7732,7731,7730,7729,7728,7727,7726,7725,7724,7723,7722,7721,7720,7719,7718,7717,7716,7715,7714,7713,7712,7711,7710,7709,7708,7707,7706,7705,7704,7703,7702,7701,7700,7699,7698,7697,7696,7695,7694,7693,7692,7691,7690,7689,7688,7687,7686,7685,7684,7683,7682,7681,7680,7679,7678,7677,7676,7675,7674,7673,7672,7671,7670,7669,7668,7667,7666,7665,7664,7663,7662,7661,7660,7659,7658,7657,7656,7655,7654,7653,7652,7651,7650,7649,7648,7647,7646,7645,7644,7643,7642,7641,7640,7639,7638,7637,7636,7635,7634,7633,7632,7631,7630,7629,7628,7627,7626,7625,7624,7623,7622,7621,7620,7619,7618,7617,7616,7615,7614,7613,7612,7611,7610,7609,7608,7607,7606,7605,7604,7603,7602,7601,7600,7599,7598,7597,7596,7595,7594,7593,7592,7591,7590,7589,7588,7587,7586,7585,7584,7583,7582,7581,7580,7579,7578,7577,7576,7575,7574,7573,7572,7571,7570,7569,7568,7567,7566,7565,7564,7563,7562,7561,7560,7559,7558,7557,7556,7555,7554,7553,7552,7551,7550,7549,7548,7547,7546,7545,7544,7543,7542,7541,7540,7539,7538,7537,7536,7535,7534,7533,7532,7531,7530,7529,7528,7527,7526,7525,7524,7523,7522,7521,7520,7519,7518,7517,7516,7515,7514,7513,7512,7511,7510,7509,7508,7507,7506,7505,7504,7503,7502,7501,7500,7499,7498,7497,7496,7495,7494,7493,7492,7491,7490,7489,7488,7487,7486,7485,7484,7483,7482,7481,7480,7479,7478,7477,7476,7475,7474,7473,7472,7471,7470,7469,7468,7467,7466,7465,7464,7463,7462,7461,7460,7459,7458,7457,7456,7455,7454,7453,7452,7451,7450,7449,7448,7447,7446,7445,7444,7443,7442,7441,7440,7439,7438,7437,7436,7435,7434,7433,7432,7431,7430,7429,7428,7427,7426,7425,7424,9009,9008,9007,9006,9005,9004,9003,9002,9001,9000,8999,8998,8997,8996,8995,8994,8993,8992,8991,8990,8989,8988,8987,8986,8985,8984,8983,8982,8981,8980,8979,8978,8977,8976,8975,8974,8973,8972,8971,8970,8969,8968,8967,8966,8965,8964,8963,8962,8961,8960,8959,8958,8957,8956,8955,8954,8953,8952,8951,8950,8949,8948,8947,8946,8945,8944,8943,8942,8941,8940,8939,8938,8937,8936,8935,8934,8933,8932,8931,8930,8929,8928,8927,8926,8925,8924,8923,8922,8921,8920,8919,8918,8917,8916,8915,8914,8913,8912,8911,8910,8909,8908,8907,8906,8905,8904,8903,8902,8901,8900,8899,8898,8897,8896,8895,8894,8893,8892,8891,8890,8889,8888,8887,8886,8885,8884,8883,8882,8881,8880,8879,8878,8877,8876,8875,8874,8873,8872,8871,8870,8869,8868,8867,8866,8865,8864,8863,8862,8861,8860,8859,8858,8857,8855,8854,8853,8852,8851,8850,8849,8848,8847,8846,8845,8844,8843,8842,8841,8840,8839,8838,8837,8836,8835,8834,8833,8832,8831,8830,8829,8828,8827,8826,8825,8824,8823,8822,8821,8820,8819,8818,8817,8816,8815,8814,8813,8812,8811,8810,8809,8808,8806,8805,8804,8803,8802,8801,8800,8799,8798,8797,8796,8795,8794,8793,8792,8791,8790,8789,8788,8787,8786,8785,8784,8783,8782,8781,8780,8779,8778,8777,8776,8775,8774,8773,8772,8771,8770,8769,8768,8767,8766,8764,8763,8762,8761,8760,8759,8758,8757,8756,8755,8754,8753,8752,8751,8750,8749,8748,8747,8746,8745,8744,8743,8742,8741,8740,8739,8738,8737,8736,8735,8734,8733,8732,8731,8730,8729,8728,8727,8726,8725,8724,8722,8721,8720,8719,8718,8717,8716,8715,8714,8713,8712,8711,8710,8709,8708,8707,8706,8705,8704,8703,8702,8701,8700,8699,8698,8697,8696,8695,8694,8693,8692,8691,8690,8689,8688,8687,8686,8685,8684,8683,8682,8681,8680,8679,8678,8677,8676,8675,8674,8673,8672,8671,8670,8669,8668,8667,8666,8665,8664,8663,8662,8661,8660,8659,8658,8657,8656,8655,8654,8653,8652,8651,8650,8649,8648,8647,8646,8644,8643,8642,8641,8640,8639,8638,8637,8636,8635,8634,8633,8632,8631,8630,8629,8628,8627,8626,8625,8624,8623,8622,8621,8620,8619,8618,8617,8616,8615,8614,8613,8612,8611,8610,8609,8608,8607,8606,8605,8604,8602,8603,8601,8600,8599,8598,8597,8596,8595,8594,8593,8592,8591,8590,8589,8588,8587,8586,8585,8584,8583,8582,8581,8580,8579,8578,8577,8576,8575,8574,8573,8572,8571,8570,8569,8568,8567,8566,8565,8564,8563,8562,8561,8560,8559,8558,8557,8556,8555,8554,8553,8552,8551,8550,8549,8548,8547,8546,8545,8544,8543,8542,8541,8540,8539,8538,8537,8536,8535,8534,8533,8532,8531,8530,8529,8528,8527,8526,8525,8524,8523,8522,8521,8520,8519,8518,8517,8516,8515,8514,8513,8512,8511,8510,8509,8508,8507,8506,8505,8504,8503,8502,8501,8500,8499,8497,8496,8495,8494,8493,8492,8491,8490,8489,8488,8487,8486,8485,8484,8483,8482,8481,8480,8479,8478,8477,8476,8475,8474,8473,8472,8471,8470,8469,8468,8467,8466,8465,8464,8463,8462,8461,8460,8459,8458,8457,8456,8455,8454,8453,8452,8451,8450,8449,8448,8447,8446,8445,8444,8443,8442,8441,8440,8439,8438,8437,8436,8435,8434,8433,8432,8431,8430,8429,8427,8428,8426,8425,8424,8423,8422,8421,8420,8419,8418,8417,8416,8415,8414,8413,8412,8411,8410,8409,8408,8407,8406,8405,8404,8403,8402,8401,8400,8399,8398,8397,8396,8395,8394,8393,8392,8391,8390,8389,8388,8387,8386,8385,8384,8383,8382,8381,8380,8379,8378,8377,8376,8375,8374,8373,8372,8371,8370,8369,8368,8367,8366,8365,8364,8363,8362,8361,8360,8359,8358,8357,8356,8355,8354,8353,8352,8351,8350,8349,8348,8347,8346,8345,8344,8343,8342,8341,8340,8339,8338,8337,8336,8335,8334,8333,8332,8331,8330,8329,8328,8327,8326,8325,8324,8323,8322,8321,8319,8318,8317,8316,8315,8314,8313,8312,8311,8310,8309,8308,8307,8306,8305,8304,8303,8302,8301,8300,8299,8298,8297,8296,8295,8294,8293,8292,8291,8290,8289,8288,8287,8286,8285,8284,8283,8282,8281,8280,8279,8278,8277,8276,8275,8274,8273,8272,8271,8270,8269,8268,8267,8266,8265,8264,8263,8262,8261,8260,8259,8258,8257,8256,8255,8254,8253,8252,8251,8250,8249,8248,8247,8246,8245,8244,8243,8242,8241,8240,8239,8238,8237,8236,8235,8234,8233,8232,8231,8230,8229,8228,8227,8226,8225,8224,8223,8222,8221,8220,8219,8218,8217,8216,8215,8214,8213,8212,8211,8210,8209,8208,8207,8206,8205,9787,9786,9785,9784,9783,9782,9781,9780,9779,9778,9777,9776,9775,9774,9773,9772,9771,9770,9769,9768,9767,9766,9765,9764,9763,9762,9761,9760,9759,9758,9757,9756,9755,9754,9753,9752,9751,9750,9749,9748,9747,9746,9745,9744,9743,9742,9741,9740,9739,9738,9737,9736,9735,9734,9733,9732,9731,9730,9729,9728,9727,9726,9725,9724,9723,9722,9721,9720,9719,9718,9717,9716,9715,9714,9713,9712,9711,9710,9709,9708,9707,9706,9705,9704,9703,9702,9701,9700,9698,9697,9696,9695,9694,9693,9692,9691,9690,9689,9688,9687,9686,9685,9684,9683,9682,9681,9680,9679,9678,9677,9676,9675,9674,9673,9672,9671,9670,9669,9668,9667,9666,9665,9664,9663,9662,9661,9660,9659,9658,9657,9656,9655,9654,9653,9652,9651,9650,9649,9648,9647,9646,9645,9644,9643,9642,9641,9640,9639,9638,9637,9636,9635,9634,9633,9632,9631,9630,9629,9628,9627,9626,9625,9624,9623,9622,9621,9620,9619,9618,9617,9616,9615,9614,9613,9612,9611,9610,9609,9608,9607,9606,9605,9604,9603,9602,9601,9600,9599,9598,9597,9596,9595,9594,9593,9592,9591,9590,9589,9588,9587,9586,9585,9584,9583,9582,9581,9580,9579,9578,9577,9576,9575,9574,9573,9572,9571,9570,9569,9568,9567,9566,9565,9564,9563,9562,9561,9560,9559,9558,9557,9556,9555,9554,9553,9552,9551,9550,9549,9548,9547,9546,9545,9544,9543,9542,9541,9540,9539,9538,9537,9536,9535,9534,9533,9532,9531,9530,9529,9528,9527,9526,9525,9524,9523,9522,9521,9520,9519,9518,9517,9516,9515,9514,9513,9512,9511,9510,9509,9508,9507,9506,9505,9504,9503,9502,9501,9500,9499,9498,9497,9496,9495,9494,9493,9492,9491,9490,9489,9488,9487,9486,9485,9484,9483,9482,9481,9480,9479,9478,9477,9476,9475,9474,9473,9472,9471,9470,9469,9468,9467,9466,9465,9464,9463,9462,9461,9460,9459,9458,9457,9456,9455,9454,9453,9452,9451,9450,9449,9448,9447,9446,9445,9444,9443,9442,9441,9439,9438,9437,9436,9435,9434,9433,9432,9431,9430,9429,9428,9427,9426,9425,9424,9423,9422,9421,9420,9419,9418,9417,9416,9415,9414,9413,9412,9411,9410,9409,9408,9407,9406,9405,9404,9403,9402,9401,9400,9399,9398,9397,9396,9395,9394,9393,9392,9391,9390,9389,9388,9387,9386,9385,9384,9383,9382,9381,9380,9379,9378,9377,9376,9375,9374,9373,9372,9371,9370,9369,9368,9367,9366,9365,9364,9363,9362,9361,9360,9359,9358,9357,9356,9355,9354,9353,9352,9351,9350,9349,9348,9347,9346,9345,9344,9343,9342,9341,9340,9339,9338,9337,9336,9335,9334,9333,9332,9331,9330,9329,9328,9327,9326,9324,9323,9322,9321,9320,9319,9318,9317,9316,9315,9314,9313,9312,9311,9310,9309,9308,9307,9306,9305,9304,9303,9302,9301,9300,9299,9298,9297,9296,9295,9294,9293,9292,9291,9290,9289,9288,9287,9286,9285,9284,9283,9282,9281,9280,9279,9278,9277,9276,9275,9274,9273,9272,9271,9270,9269,9268,9267,9266,9265,9264,9263,9262,9261,9260,9259,9258,9257,9256,9255,9254,9253,9252,9251,9250,9249,9248,9247,9246,9244,9243,9241,9240,9239,9238,9237,9236,9235,9234,9233,9232,9231,9230,9229,9228,9227,9226,9225,9224,9223,9222,9221,9220,9219,9218,9217,9216,9215,9214,9213,9212,9211,9210,9209,9208,9207,9206,9205,9204,9203,9202,9201,9200,9199,9198,9197,9196,9195,9194,9193,9192,9191,9190,9189,9188,9186,9185,9184,9183,10478,10477,10476,10475,10474,10473,10472,10471,10470,10469,10468,10467,10466,10465,10464,10463,10462,10461,10460,10459,10458,10457,10456,10455,10454,10453,10452,10451,10450,10449,10448,10447,10446,10445,10444,10443,10442,10441,10440,10439,10438,10437,10436,10435,10434,10433,10432,10431,10430,10429,10428,10427,10426,10425,10424,10423,10422,10421,10420,10419,10418,10417,10416,10415,10414,10413,10412,10411,10410,10409,10408,10407,10406,10405,10404,10403,10402,10401,10400,10399,10398,10397,10396,10395,10394,10393,10392,10391,10390,10389,10388,10387,10386,10385,10384,10383,10382,10381,10380,10379,10378,10377,10376,10375,10374,10373,10372,10371,10370,10369,10368,10367,10366,10365,10364,10363,10362,10361,10360,10359,10358,10357,10356,10355,10354,10353,10352,10351,10350,10349,10348,10347,10346,10345,10344,10343,10342,10341,10340,10339,10338,10337,10336,10335,10334,10333,10332,10331,10330,10329,10328,10327,10326,10325,10324,10323,10322,10321,10320,10319,10318,10317,10316,10315,10314,10313,10312,10311,10310,10309,10308,10307,10306,10305,10304,10303,10302,10301,10300,10299,10298,10297,10296,10295,10294,10293,10292,10291,10290,10289,10288,10287,10286,10285,10284,10283,10282,10281,10280,10279,10278,10277,10276,10275,10274,10273,10272,10271,10270,10269,10268,10267,10266,10265,10264,10263,10262,10261,10260,10259,10258,10257,10256,10255,10254,10253,10252,10251,10250,10249,10248,10247,10246,10245,10244,10243,10242,10241,10240,10239,10238,10237,10236,10235,10234,10233,10232,10231,10230,10229,10228,10227,10226,10225,10224,10223,10222,10221,10220,10219,10218,10217,10216,10215,10214,10213,10212,10211,10210,10209,10208,10207,10206,10205,10204,10203,10201,10200,10199,10198,10197,10196,10195,10194,10193,10192,10191,10190,10189,10188,10187,10186,10185,10184,10183,10182,10181,10180,10179,10178,10177,10176,10175,10174,10173,10172,10171,10170,10169,10168,10167,10166,10165,10164,10163,10162,10161,10160,10159,10158,10157,10156,10155,10154,10153,10152,10151,10150,10149,10148,10147,10146,10145,10144,10143,10142,10141,10140,10139,10138,10137,10136,10135,10134,10133,10132,10131,10130,10129,10128,10127,10126,10125,10124,10123,10121,10120,10119,10118,10116,10115,10114,10113,10112,10111,10110,10109,10108,10107,10106,10105,10104,10103,10102,10101,10100,10099,10098,10097,10096,10095,10094,10093,10092,10091,10090,10089,10088,10087,10086,10085,10084,10083,10082,10081,10080,10079,10078,10077,10076,10075,10074,10073,10072,10071,10070,10069,10068,10067,10066,10065,10064,10063,10062,10061,10060,10059,10058,10057,10056,10055,10054,10053,10052,10051,10050,10049,10048,10047,10046,10045,10044,10043,10042,10041,10040,10039,10038,10037,10035,10036,10034,10033,10031,10030,10029,10028,10027,10026,10025,10024,10023,10022,10021,10020,10019,10018,10017,10016,10015,10014,10013,10012,10011,10010,10009,10008,10007,10006,10005,10004,10003,10002,10001,10000,9999,9998,9997,9996,9995,9994,9993,9992,9991,9990,9989,9988,9987,9986,9985,9984,9983,9982,9981,9980,9979,9978,9977,9976,9975,9974,9973,9972,9971,9970,9969,9968,9967,9966,9965,9964,9963,9962,9961,9960,9959,9958,9957,9956,9955,9954,9953,9952,9951,9950,9949,9948,9947,9946,9945,9944,9943,9942,9941,9940,9939,9938,9937,9936,9935,9934,9933,9932,9931,9930,9929,9928,9927,9926,9925,9924,9923,9922,9921,9920,9919,9918,9917,9916,9915,9914,9913,9912,9911,9910,9909,9908,9907,9906,9905,9904,9902,9901,9900,9899,9898,9897,9896,9895,9894,9893,9892,9891,9890,9889,9888,9887,9886,9885,9884,9883,9882,9881,9880,9879,9878,9877,9876,9875,9874,9873,9872,9871,9870,9869,9868,9867,9866,9865,9864,9863,9862,9861,9860,9859,9858,9857,9856,9855,9854,9853,9852,9851,9850,9849,9848,9847,9846,9845,9844,9843,9842,9841,9840,9838,9837,9836,9835,9834,9833,9832,9831,9830,9829,9828,9827,9826,9825,9824,9823,9822,9821,9820,9819,9818,9817,9816,9815,9814,9813,9812,9811,9810,9809,9808,9807,9806,9805,9804,9803,9802,9801,9800,9799,9798,9797,9796,9795,9794,9793,9792,9791,9790,9789,9788,11315,11314,11313,11312,11311,11310,11309,11308,11307,11306,11305,11304,11303,11302,11301,11300,11299,11298,11296,11295,11294,11293,11292,11291,11290,11289,11288,11287,11286,11285,11284,11283,11282,11281,11280,11279,11278,11277,11276,11275,11274,11273,11272,11271,11270,11269,11268,11267,11266,11265,11264,11263,11262,11261,11260,11259,11258,11257,11256,11255,11254,11253,11252,11251,11250,11249,11248,11247,11246,11245,11244,11243,11242,11241,11240,11239,11238,11237,11236,11235,11234,11233,11232,11231,11230,11229,11228,11227,11226,11224,11221,11220,11219,11218,11217,11216,11215,11214,11213,11211,11210,11209,11208,11207,11206,11205,11204,11203,11202,11201,11200,11199,11198,11197,11196,11195,11194,11193,11192,11191,11190,11189,11188,11187,11186,11185,11184,11183,11182,11181,11180,11179,11178,11177,11176,11175,11174,11173,11172,11171,11170,11169,11168,11167,11166,11165,11164,11163,11162,11161,11160,11159,11158,11157,11156,11155,11154,11153,11152,11151,11150,11149,11148,11147,11146,11145,11144,11143,11142,11141,11140,11139,11138,11137,11136,11135,11134,11133,11132,11131,11130,11129,11128,11127,11126,11125,11124,11123,11122,11121,11120,11119,11118,11117,11116,11115,11114,11113,11112,11111,11110,11109,11108,11107,11106,11105,11104,11103,11102,11101,11100,11099,11098,11097,11096,11095,11094,11093,11092,11091,11090,11089,11088,11087,11086,11085,11084,11083,11082,11081,11080,11079,11078,11077,11076,11075,11074,11073,11072,11071,11070,11069,11068,11067,11066,11065,11064,11063,11062,11061,11060,11059,11058,11057,11056,11055,11054,11053,11052,11051,11050,11049,11048,11047,11046,11045,11044,11043,11042,11041,11040,11039,11038,11037,11036,11035,11034,11033,11032,11031,11030,11029,11028,11027,11026,11025,11024,11023,11022,11021,11020,11019,11018,11017,11016,11015,11014,11013,11012,11011,11010,11009,11008,11007,11006,11005,11004,11003,11002,11001,11000,10999,10998,10997,10996,10995,10994,10993,10992,10991,10990,10989,10988,10987,10986,10985,10984,10983,10982,10981,10980,10979,10978,10977,10976,10975,10974,10973,10972,10971,10970,10969,10968,10967,10966,10965,10964,10963,10962,10961,10960,10959,10958,10957,10956,10955,10954,10953,10952,10951,10950,10949,10948,10947,10946,10945,10944,10943,10942,10941,10940,10939,10938,10937,10936,10935,10934,10933,10932,10931,10930,10929,10928,10927,10926,10925,10924,10923,10922,10921,10920,10919,10918,10917,10916,10915,10914,10913,10912,10911,10910,10909,10908,10907,10906,10905,10904,10903,10902,10901,10900,10899,10898,10897,10896,10895,10894,10893,10892,10891,10890,10889,10888,10887,10886,10885,10884,10883,10882,10881,10880,10879,10878,10877,10876,10875,10874,10873,10872,10871,10870,10869,10868,10867,10866,10865,10864,10863,10862,10861,10860,10859,10858,10857,10856,10855,10854,10853,10852,37692,10850,10849,10848,10847,10846,10845,10844,10843,10842,10841,10840,10839,10838,10837,10836,10835,10834,10833,10832,10831,10830,10829,10828,10827,10826,10825,10824,10823,10822,10821,10820,10819,10818,10817,10816,10815,10814,10813,10812,10811,10810,10809,10808,10807,10806,10805,10804,10803,10802,10801,10800,10799,10798,10797,10795,10794,10793,10792,10791,10790,10789,10788,10787,10786,10785,10784,10783,10782,10781,10780,10779,10778,10777,10776,10775,10774,10773,10772,10771,10770,10769,10768,10767,10766,10765,10764,10763,10762,10761,10760,10759,10758,10757,10756,10755,10754,10753,10752,10751,10750,10749,10748,10747,10746,10745,10744,10743,10742,10741,10740,10739,10738,10737,10736,10735,10734,10733,10732,10731,10730,10729,10728,10727,10726,10725,10724,10723,10722,10721,10720,10719,10718,10717,10716,10715,10714,10713,10712,10711,10710,10709,10708,10707,10706,10705,10704,10703,10702,10701,10700,10699,10698,10697,10696,10695,10694,10693,10692,10691,10690,10689,10688,10687,10686,10685,10684,10683,10682,10681,10680,10679,10678,10677,10676,10675,10674,10673,10672,10671,10670,10669,10668,10667,10666,10665,10664,10663,10662,10661,10660,10659,10658,10657,10656,10655,10654,10653,10652,10651,10650,10649,10648,10647,10646,10645,10644,10643,10642,10641,10640,10639,10638,10637,10636,10635,10634,10633,10632,10631,10630,10629,10628,10627,10626,10625,10624,10623,10622,10621,10620,10619,10618,10617,10616,10615,10614,10613,10612,10611,10610,10609,10608,10607,10606,10605,10604,10603,10602,10601,10600,10599,10598,10597,10596,10595,10594,10593,10592,10591,10590,10589,10588,10587,10586,10585,10584,10583,10582,10581,10580,10579,10578,10577,10576,10575,10574,10573,10572,10571,10570,10569,10568,10567,10566,10565,10564,10563,10562,10561,10560,10559,10558,10557,10556,10555,10554,10553,10552,10551,10550,10549,10548,10547,10546,10545,10544,10543,10542,10541,10540,10539,10538,10537,10536,10535,10534,10533,10532,10531,10530,10529,10528,10527,10526,10525,10524,10523,10522,10521,10520,10519,10518,10517,10516,10515,10514,10513,10512,10511,10510,10509,10508,10507,10506,10505,10504,10503,10502,10501,10500,10499,10498,10497,10496,10495,10494,10493,10492,10491,10490,10489,10488,10487,10486,10485,10484,10483,10482,10481,10480,10479,12263,12262,12261,12260,12259,12258,12257,12256,12255,12254,12253,12252,12251,12250,12249,12248,12247,12246,12245,12244,12243,12242,12241,12240,12239,12238,12237,12236,12235,12234,12233,12232,12231,12230,12229,12228,12227,12226,12225,12224,12223,12222,12221,12220,12219,12218,12217,12216,12215,12214,12213,12212,12211,12210,12209,12208,12207,12206,12205,12204,12203,12202,12201,12200,12199,12198,12197,12196,12195,12194,12193,12192,12191,12190,12189,12188,12187,12186,12185,12184,12183,12182,12181,12180,12179,12178,12177,12176,12175,12174,12173,12172,12171,12170,12169,12168,12167,12166,12165,12164,12163,12162,12161,12160,12159,12158,12157,12156,12155,12154,12153,12152,12151,12150,12149,12148,12147,12146,12145,12144,12143,12142,12141,12140,12139,12138,12137,12136,12135,12134,12133,12132,12131,12130,12129,12128,12127,12126,12125,12124,12123,12122,12121,12120,12119,12118,12117,12116,12115,12114,12113,12112,12111,12110,12109,12108,12107,12106,12105,12104,12103,12102,12101,12100,12099,12098,12097,12096,12095,12094,12093,12092,12091,12090,12089,12088,12087,12086,12085,12084,12083,12082,12081,12080,12079,12078,12077,12076,12075,12074,12073,12071,12070,12069,12068,12067,12066,12065,12064,12063,12062,12061,12060,12059,12058,12057,12056,12055,12054,12053,12052,12051,12050,12049,12048,12047,12046,12045,12044,12043,12042,12041,12040,12039,12038,12037,12036,12035,12034,12033,12032,12031,12030,12029,12028,12027,12026,12025,12024,12023,12022,12021,12020,12019,12018,12017,12016,12015,12014,12013,12012,12011,12010,12009,12008,12007,12006,12005,12004,12003,12002,12001,12000,11999,11998,11997,11996,11995,11994,11993,11992,11991,11990,11989,11988,11987,11986,11985,11984,11983,11982,11981,11980,11979,11978,11977,11976,11975,11974,11973,11972,11971,11970,11969,11968,11967,11966,11965,11964,11963,11962,11961,11960,11959,11958,11957,11956,11955,11954,11953,11952,11951,11950,11949,11948,11947,11946,11945,11944,11943,11942,11941,11940,11939,11938,11937,11936,11935,11934,11933,11932,11931,11930,11929,11928,11927,11926,11925,11924,11923,11922,11921,11920,11919,11918,11917,11916,11915,11914,11913,11912,11911,11910,11909,11908,11907,11906,11905,11904,11903,11902,11901,11900,11899,11898,11897,11896,11895,11894,11893,11892,11891,11890,11889,11888,11887,11886,11885,11884,11883,11882,11881,11880,11879,11878,11877,11876,11875,11874,11873,11872,11871,11870,11869,11868,11867,11866,11865,11864,11863,11862,11861,11860,11859,11858,11857,11856,11855,11854,11853,11852,11851,11850,11849,11848,11847,11846,11845,11844,11843,11842,11841,11840,11839,11838,11837,11836,11835,11834,11833,11832,11831,11830,11829,11828,11827,11826,11825,11824,11823,11822,11821,11820,11819,11818,11817,11816,11815,11814,11813,11812,11811,11810,11809,11808,11807,11806,11805,11804,11803,11802,11801,11800,11799,11798,11797,11796,11795,11794,11793,11792,11791,11790,11789,11788,11787,11786,11785,11784,11783,11782,11781,11780,11779,11778,11777,11776,11775,11774,11773,11772,11771,11770,11769,11768,11767,11766,11765,11764,11763,11762,11761,11760,11759,11758,11757,11755,11754,11753,11752,11751,11750,11749,11748,11747,11746,11745,11744,11743,11742,11741,11740,11739,11738,11737,11736,11735,11734,11733,11732,11731,11730,11729,11728,11727,11726,11725,11724,11723,11722,11721,11720,11719,11718,11717,11716,11715,11714,11713,11712,11711,11710,11709,11708,11707,11706,11705,11704,11703,11702,11701,11700,11699,11698,11697,11696,11695,11694,11693,11692,11691,11690,11689,11688,11687,11686,11685,11684,11683,11682,11681,11680,11679,11678,11677,11676,11675,11674,11673,11672,11671,11670,11669,11668,11667,11666,11665,11664,11663,11662,11661,11660,11659,11658,11657,11656,11655,11654,11653,11652,11651,11650,11649,11648,11647,11646,11645,11644,11643,11642,11641,11640,11639,11638,11637,11636,11635,11634,11633,11632,11631,11630,11629,11628,11627,11626,11625,11624,11623,11622,11621,11620,11619,11618,11617,11616,11615,11614,11613,11612,11611,11610,11609,11608,11607,11606,11605,11604,11603,11602,11601,11600,11599,11598,11597,11596,11595,11594,11593,11592,11591,11590,11589,11588,11587,11586,11585,11584,11583,11582,11581,11580,11579,11578,11577,11576,11575,11574,11573,11572,11571,11570,11569,11568,11567,11566,11565,11564,11563,11562,11561,11560,11559,11558,11557,11556,11555,11554,11553,11552,11551,11550,11549,11548,11547,11546,11545,11544,11543,11542,11541,11540,11539,11538,11537,11536,11535,11534,11533,11532,11531,11530,11529,11528,11527,11526,11525,11524,11523,11522,11521,11520,11519,11518,11517,11516,11515,11514,11513,11512,11511,11510,11509,11508,11507,11506,11505,11504,11503,11502,11501,11500,11499,11498,11497,11496,11495,11494,11493,11492,11491,11490,11489,11488,11487,11486,11485,11484,11483,11482,11481,11480,11479,11478,11477,11476,11475,11474,11473,11472,11471,11470,11469,11468,11467,11466,11465,11464,11463,11462,11461,11460,11459,11458,11457,11456,11455,11454,11453,11452,11451,11450,11449,11448,11447,11446,11445,11444,11443,11442,11441,11440,11439,11438,11437,11436,11435,11434,11433,11432,11431,11430,11429,11428,11427,11426,11425,11424,11423,11422,11421,11420,11419,11418,11417,11416,11415,11414,11413,11412,11411,11410,11409,11408,11407,11406,11405,11404,11403,11402,11401,11400,11399,11398,11397,11396,11395,11394,11393,11392,11391,11390,11389,11388,11387,11386,11385,11384,11383,11382,11381,11380,11379,11378,11377,11376,11375,11374,11373,11372,11371,11370,11369,11368,11367,11366,11365,11364,11363,11362,11361,11360,11359,11358,11357,11356,11355,11354,11353,11352,11351,11350,11349,11348,11347,11346,11345,11344,11343,11342,11341,11340,11339,11338,11337,11336,11335,11334,11333,11332,11331,11330,11329,11328,11327,11326,11325,11324,11323,11322,11321,11320,11319,11318,11317,11316,13165,13164,13163,13162";
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

                    xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsCorefx_05112019.xml");
                    MessageBox.Show("Done");
                    #endregion Issues
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsCorefx_05112019.xml");
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
