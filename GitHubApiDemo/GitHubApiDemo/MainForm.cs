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
                          GetIssueDetails();
                        //RemoveCodeFromText();
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

            //string owner = "dotnet";
            //string name = "corefx";


            // string owner = "SVF-tools";
            //string name = "SVF";


            string owner = "dotnet";
            string name = "coreclr";

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailscoreclr_19112019.xml");
            XmlNode rootNode = xmlDoc["IssueDetails"];
            try
            {
                var Contributors = GetContributors(owner, name);
                if (!Contributors.IsCompleted)
                {
                    var collaboratorresponse = await Contributors.ConfigureAwait(false);
                    IReadOnlyList<RepositoryContributor> _collaboratorresponse = collaboratorresponse as IReadOnlyList<RepositoryContributor>;
                    List<string> collaborators = _collaboratorresponse?.Select(x => x.Login).ToList();

                    string issuedIDs = "19819,19818,19817,19816,19815,19814,19813,19812,19811,19810,19809,19808,19807,19806,19805,19804,19778,19777,19776,19775,19774,19773,19772,19771,19770,19769,19768,19767,19766,19765,19764,19763,19762,19761,19760,19759,19758,19757,19756,19755,19754,19753,19752,19751,19750,19749,19748,19747,19746,19745,19744,19743,19742,19741,19740,19739,19738,19737,19736,19735,19734,19733,19732,19731,19730,19729,19728,19727,19726,19725,19724,19723,19722,26625,26624,26623,19721,19720,19719,19718,19717,19716,19715,19714,19713,19712,19711,19710,19709,19708,19707,19706,19705,19704,19703,19702,19701,19700,19699,19698,19697,19696,19695,19694,19693,19692,19691,19690,19689,19688,19687,19686,19685,19684,19683,19682,19681,19680,19679,19678,19677,19676,19675,19674,19673,19672,19671,19670,19669,19668,19667,19666,19665,19664,19663,19662,19661,19660,19659,19658,19657,19656,19655,19654,19653,19652,19651,19650,19649,19648,19647,19646,19645,19644,19643,19642,19641,19640,19639,19638,19637,19636,19635,19634,19633,19632,19631,19630,19629,19628,19627,19625,19624,19623,19622,19621,19620,19619,19618,19617,19616,19615,19614,19613,19612,19611,19610,19609,19608,19607,19606,19605,19604,19603,19602,19601,19600,19599,19598,19597,19596,19595,19594,19593,19592,19591,19590,19589,19588,19587,19586,19585,19584,19583,19582,19581,19580,19579,19578,19577,19576,19575,19574,19573,19572,19571,19570,19569,19568,19567,19566,19565,19564,19563,19562,19561,19560,19559,19558,19557,19556,19555,19554,19553,19552,19551,19550,19549,19548,19547,19546,19545,19544,19543,19542,19541,19540,19539,19538,19537,19536,19535,19534,19533,19532,19531,19530,19529,19528,19527,19526,19525,19524,19523,19522,19521,19520,19519,19518,19517,19516,19515,19514,19513,19512,19511,19510,19509,19508,19507,19506,19505,19504,19503,19502,19501,19500,19499,19498,19497,19496,19495,19494,19493,19492,19491,19490,19489,19488,19487,19486,19485,19484,19483,19482,19481,19480,19479,19478,19477,19476,19475,19474,19473,19472,19471,19470,19469,19468,19467,19466,19465,19464,19463,19462,19461,19460,19459,19458,19457,19456,19455,19454,19453,19452,19451,19450,19449,19448,19447,19446,19445,19444,19443,19442,19441,19440,19439,19438,19437,19436,19435,19434,19433,19432,19431,19430,19429,19428,19427,19426,19425,19424,19423,19422,19421,19420,19419,19418,19417,19416,19415,19414,19413,19412,19411,19410,19409,19408,19407,19406,19405,19404,19403,19402,19401,19400,19399,19398,19397,19396,19395,19394,19393,19392,19391,19390,19389,19388,19387,19386,19385,19384,19383,19382,19381,19380,19379,19378,19377,19376,19375,19374,19373,19372,19371,19370,19369,19368,19367,19366,19365,19364,19363,19362,19361,19360,19359,19358,19357,19356,19355,19354,19353,19352,19351,19350,19349,19348,19347,19346,19345,19344,19343,19342,19341,19340,19339,19338,19337,19336,19335,19334,19333,19332,19331,19330,19329,19328,19327,19326,19325,19324,19323,19322,19321,19320,19319,19318,19317,19316,19315,19314,19313,19312,19311,19310,19309,19308,19307,19306,19305,19304,19303,19302,19301,19300,19299,19298,19297,19296,19295,19294,19293,19292,19291,19290,19289,19288,19287,19286,19285,19284,19283,19282,19281,19280,19279,19278,19277,19276,19275,19274,19273,19272,19271,19270,19269,19268,19267,19266,19265,19264,19263,19262,19261,19260,19259,19258,19257,19256,19255,19254,19252,19251,19250,19249,19248,19247,19246,19245,19244,19243,19242,19241,19240,19239,19238,19237,19236,19235,19234,19233,19232,19231,19230,19229,19228,19227,19226,19206,19205,19204,19203,19202,19201,19200,19199,19198,19197,19196,19195,19194,19193,19192,19191,19190,19189,19188,19187,19186,19185,19184,19183,19182,19181,19180,19179,19178,19177,19176,19175,19174,19173,19172,19171,19170,19169,19168,19167,19166,19165,19164,19163,19162,19161,19160,19159,19158,19157,19156,19155,19154,19153,19152,19151,19150,19149,19148,19147,19146,19145,19144,19143,19142,19141,19140,19139,19138,19137,19136,19135,19134,19133,19132,19131,19130,19129,19128,19127,19126,19125,19124,19123,19122,19121,19120,19119,19118,19117,19116,19115,19114,19113,19112,19111,19110,19109,19108,19107,19106,19105,19104,19103,19102,19101,19100,19099,19098,19097,19096,19095,19094,19093,19092,19091,19090,19089,19088,19087,19086,19085,19084,19083,19082,19081,19080,19079,19078,19077,19076,19075,19074,19073,19072,19071,19070,19069,19068,19067,19066,19065,19064,19063,19062,19061,19060,19059,19058,19057,19056,19055,19054,19053,19052,19051,19050,19049,19048,19047,19046,19045,19044,19043,19042,19041,19040,19039,19038,19037,19036,19035,19034,19033,19032,19031,19030,19029,19028,19027,19026,19025,19024,19023,19022,19021,19020,19019,19018,19017,19016,19015,19014,19013,19012,19011,19010,19009,19008,19007,19006,19005,19004,19003,19002,19001,19000,18999,18998,18997,18996,18995,18994,18993,18992,18991,18990,18989,18988,18987,18986,18985,18984,18983,18982,18981,18980,18979,18978,18977,18976,18975,18974,18973,18972,18971,18970,18969,18968,18967,18966,18965,18964,18963,18962,18961,18960,18959,18958,18957,18956,18955,18954,18953,18952,18951,18950,18949,18948,18947,18946,18945,18944,18943,18942,18941,18940,18939,18938,18937,18936,18935,18934,18933,18932,18931,18930,18929,18928,18927,18926,18925,18924,18923,18922,18921,18920,18919,18918,18917,18916,18915,18914,18913,18912,18911,18910,18909,18908,18907,18906,18905,18904,18903,18902,18901,18900,18899,18898,18897,18896,18895,18894,18893,18892,18891,18890,18889,18888,18887,18886,18885,18884,18883,18882,18881,18880,18879,18878,18877,18876,18875,18874,18873,18872,18871,18870,18869,18868,18867,18866,18865,18864,18863,18862,18861,18860,18859,18858,18857,18856,18855,18854,18853,18852,18851,18850,18849,18848,18847,18846,18845,18844,18843,18842,18841,18840,18839,18838,18837,18836,18835,18834,18833,18832,18831,18830,18829,18828,18827,18826,18825,18824,18823,18822,18821,18820,18819,18818,18817,18816,18815,18814,18813,18812,18811,18810,18809,18808,18807,18806,18805,18804,18803,18802,18801,18800,18799,18798,18797,18796,18795,18794,18793,18792,18791,18790,18789,18788,18787,18786,18785,18784,18783,18782,18781,18780,18779,18778,18777,18776,18775,18774,18773,18772,18771,18770,18769,18768,18767,18766,18765,18764,18763,18762,18761,18760,18759,18758,18757,18756,18755,18754,18753,18752,18751,18750,18749,18748,18747,18746,18745,18744,18743,18742,18741,18740,18739,18738,18737,18736,18735,18734,18732,18731,18730,18729,18728,18727,18726,18725,18724,18723,18722,18721,18720,18719,18718,18717,18716,18715,18714,18713,18712,18711,18710,18709,18708,18707,18706,18705,18704,18703,18702,18701,18700,18699,18698,18697,18696,18695,18694,18693,18692,18691,18690,18689,18688,18687,18686,18685,18684,18683,18682,18681,18680,18679,18678,18677,18676,18675,18674,18673,18672,18671,18670,18669,18668,18667,18666,18665,18664,18663,18662,18661,18660,18659,18658,18657,18656,18655,18654,18653,18652,18651,18650,18649,18648,18647,18646,18645,18644,18643,18642,18641,18640,18639,18638,18637,18636,18635,18634,18633,18632,18631,18630,18629,18628,18627,18626,18625,18624,18623,18622,18621,18620,18619,18618,18617,18616,18615,18614,18613,18612,18611,18610,18609,18608,18607,18606,18605,18604,18603,18602,18601,18600,18599,18598,18597,18596,18595,18594,18593,18592,18591,18590,18589,18588,18587,18586,18585,18584,18583,18582,18581,18580,18579,18578,18577,18576,18575,18574,18573,18572,18571,18570,18569,18568,18567,18566,18565,18564,18563,18562,18561,18560,18559,18558,18557,18556,18555,18554,18553,18552,18551,18550,18549,18548,18547,18546,18545,18544,18543,18542,18541,18540,18539,18538,18537,18536,18535,18534,18533,18532,18531,18530,18529,18528,18527,18526,18525,18524,18523,18522,18521,18520,18519,18518,18517,18516,18515,18514,18513,18512,18511,18510,18509,18508,18507,18506,18505,18504,18503,18502,18501,18500,18499,18498,18497,18496,18495,18494,18493,18492,18491,18490,18489,18488,18487,18486,18485,18483,18482,18481,18480,18479,18478,18477,18476,18475,18474,18473,18472,18471,18470,18469,18468,18467,18466,18465,18464,18463,18462,18461,18460,18459,18458,18457,18456,18455,18454,18453,18452,18451,18450,18449,18448,18447,18446,18445,18444,18443,18442,18441,18440,18439,18438,18437,18436,18435,18434,18433,18432,18431,18430,18429,18428,18427,18426,18425,18424,18423,18422,18421,18420,18419,18418,18417,18416,18415,18414,18413,18412,18411,18410,18409,18408,18407,18406,18405,18404,18403,18402,18401,18400,18399,18398,18397,18396,18395,18394,18393,18392,18391,18390,18389,18388,18387,18386,18385,18384,18383,18382,18381,18380,18379,18378,18377,18376,18375,18374,18373,18372,18371,18370,18369,18368,18367,18366,18365,18364,18363,18362,18361,18360,18359,18358,18357,18356,18355,18354,18353,18352,18351,18350,18349,18348,18347,18346,18345,18344,18343,18342,18341,18340,18339,18338,18337,18336,18335,18334,18333,18332,18331,18330,18329,18328,18327,18326,18325,18324,18323,18322,18321,18320,18319,18318,18317,18316,18315,18314,18313,18312,18311,18310,18309,18308,18307,18306,18305,18304,18303,18302,18301,18300,18299,18298,18297,18296,18295,18294,18293,18292,18291,18290,18289,18288,18287,18286,18285,18284,18283,18282,18281,18280,18279,18278,18277,18276,18275,18274,18273,18272,18271,18270,18269,18268,18267,18266,18265,18264,18263,18262,18261,18260,18259,18258,18257,18256,18255,18254,18253,18252,18251,18250,18249,18248,18247,18246,18245,18244,18243,18242,18241,18240,18239,18238,18237,18236,18235,18234,18233,18232,18230,18229,18228,18210,18209,18208,18207,18206,18205,18204,18203,18202,18201,18200,18199,18198,18197,18196,18195,18194,18193,18192,18191,18190,18189,18188,18187,18186,18185,18184,18183,18182,18181,18180,18179,18178,18177,18176,18175,18174,18173,18172,18171,18170,18169,18168,18167,18166,18165,18164,18163,18162,18161,18160,18159,18158,18157,18156,18155,18154,18153,18152,18151,18150,18149,18148,18147,18146,18145,18144,18143,18142,18141,18140,18139,18138,18137,18136,18135,18134,18133,18132,18131,18130,18129,18128,18127,18126,18125,18124,18123,18122,18121,18120,18119,18118,18117,18116,18115,18114,18113,18112,18111,18110,18109,18108,18107,18106,18105,18104,18103,18102,18101,18100,18099,18098,18097,18096,18095,18094,18093,18092,18091,18090,18089,18088,18087,18086,18085,18084,18083,18082,18081,18080,18079,18078,18077,18076,18075,18074,18073,18072,18071,18070,18069,18068,18067,18066,18065,18064,18063,18062,18061,18060,18059,18058,18057,18056,18055,18054,18053,18052,18051,18050,18049,18048,18047,18046,18045,18044,18043,18042,18041,18040,18039,18038,18037,18036,18035,18034,18033,18032,18031,18030,18029,18028,18027,18026,18025,18024,18023,18022,18021,18020,18019,18018,18017,18016,18015,18014,18013,18012,18011,18010,18009,18008,18007,18006,18005,18004,18003,18002,18001,18000,17999,17998,17997,17996,17995,17994,17993,17992,17991,17990,17989,17988,17987,17986,17985,17984,17983,17982,17980,17979,17978,17977,17974,17973,17972,17971,17970,17969,17968,17967,17966,17965,17964,17963,17962,17961,17960,17959,17958,17957,17956,17955,17954,17953,17952,17951,17950,17949,17948,17947,17946,17945,17944,17943,17942,17941,17940,17939,17938,17937,17936,17935,17934,17933,17932,17931,17930,17929,17928,17927,17926,17925,17924,17923,17922,17921,17920,17919,17918,17917,17916,17915,17914,17913,17912,17911,17910,17909,17846,17845,17844,17843,17842,17841,17840,17839,17838,17837,17836,17835,17834,17833,17832,17831,17830,17829,17828,17827,17826,17825,17824,17823,17822,17821,17820,17819,17818,17817,17816,17815,17814,17813,17812,17811,17810,17809,17808,17807,17806,17805,17804,17803,17802,17801,17800,17799,17798,17797,17796,17795,17794,17793,17792,17791,17790,17789,17788,17787,17786,17785,17784,17783,17782,17781,17780,17779,17778,17777,17776,17775,17774,17773,17772,17771,17770,17769,17768,17767,17766,17765,17764,17763,17762,17761,17760,17759,17758,17757,17756,17755,17754,17753,17752,17751,17750,17749,17748,17747,17746,17745,17744,17743,17742,17741,17740,17739,17738,17737,17736,17735,17734,17733,17732,26685,17731,17729,17728,17726,17725,17724,17723,17722,17721,17720,17719,17718,17717,17716,17715,17714,17713,17712,17711,17710,17709,17708,17707,17706,17705,17704,17703,17702,17701,17700,17699,17698,17697,17696,17695,17694,17693,17692,17691,17690,17689,17688,17687,17686,17685,17684,17683,17682,17681,17680,17679,17678,17677,17676,17675,17674,17673,17672,17671,17670,17669,17668,17667,17666,17665,17664,17663,17662,17661,17660,17659,17658,17657,17656,17655,17654,17653,17652,17651,17650,17649,17648,17647,17646,17645,17644,17643,17642,17641,17640,17639,17638,17637,17636,17635,17634,17633,17632,17631,17630,17629,17628,17627,17626,17625,17624,17623,17622,17621,17620,17619,17618,17617,17616,17615,17614,17613,17612,17611,17610,17609,17608,17607,17606,17605,17604,17603,17602,17601,17600,17599,17598,17597,17596,17595,17594,17593,17592,17591,17590,17589,17588,17587,17586,17585,17584,17583,17582,17581,17580,17579,17578,17577,17576,17575,17574,17573,17572,17571,17570,17569,17568,17567,17566,17565,17564,17563,17562,17561,17560,17559,17558,17557,17556,17555,17554,17553,17552,17551,17550,17549,17548,17547,17546,17545,17544,17543,17542,17541,17540,17539,17538,17537,17536,17535,17534,17533,17532,17531,17530,17529,17528,17527,17526,17525,17524,17523,17522,17521,17520,17519,17518,17517,17516,17515,17514,17513,17512,17511,17510,17509,17508,17507,17506,17505";
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

                    xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailscoreclr_19112019.xml");
                    MessageBox.Show("Done");
                    #endregion Issues
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailscoreclr_19112019.xml");
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
            string xmlPath = @"C:\PhD\Workbrench\GitHub_NeturalNetworks\Datasets\IssueDetailsCorefx_05112019_2_RemoveCode.xml";
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

                    int startIndex_D = _valueD.IndexOf("`");
                    int endIndex_D = _valueD.LastIndexOf("`");
                    int length_D = endIndex_D - startIndex_D + 1;

                    if (startIndex_D > -1 && endIndex_D > -1)
                    {
                        _valueD = _valueD.Remove(startIndex_D, length_D);
                        node.ChildNodes[2].InnerText = _valueD;
                    }

                    /*Remove Code From Title_Description*/
                    var _value = node.ChildNodes[3].InnerText;

                    int startIndex = _value.IndexOf("`");
                    int endIndex = _value.LastIndexOf("`");
                    int length = endIndex - startIndex + 1;

                    if (startIndex > -1 && endIndex > -1)
                    {
                        _value = _value.Remove(startIndex, length);
                        node.ChildNodes[3].InnerText = _value;
                    }
                }
                xmlDoc.Save(@"C:\PhD\Workbrench\GitHub_NeturalNetworks\Datasets\IssueDetailsCorefx_05112019_2_RemoveCode.xml");
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
