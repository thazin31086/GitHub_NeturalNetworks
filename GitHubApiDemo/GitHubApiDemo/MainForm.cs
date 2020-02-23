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
using System.Text.RegularExpressions;
using System.Collections;

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
        private static GitHubClient client;
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
                        // RemoveCodeFromText();
                        //CounteNoofIssueswithCodes();
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

            //string owner = "dotnet";
            //string name = "coreclr";

            //string owner = "dotnet";
            //string name = "orleans";

            string owner = "dotnet";
            string name = "cli";

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailscli_19112019.xml");
            XmlNode rootNode = xmlDoc["IssueDetails"];
            try
            {
                var Contributors = GetContributors(owner, name);
                if (!Contributors.IsCompleted)
                {
                    var collaboratorresponse = await Contributors.ConfigureAwait(false);
                    IReadOnlyList<RepositoryContributor> _collaboratorresponse = collaboratorresponse as IReadOnlyList<RepositoryContributor>;
                    List<string> collaborators = _collaboratorresponse?.Select(x => x.Login).ToList();

                    string issuedIDs = "9103,9102,9101,9100,9099,9098,9097,9096,9095,9094,9093,9092,9091,9090,9089,9088,9087,9086,9084,9083,9082,9080,9079,9078,9077,9076,9075,9074,9073,9072,9071,9070,9069,9068,9067,9066,9065,9064,9063,9061,9059,9058,9057,9056,9055,9054,9053,9052,9051,9050,9049,9048,9047,9046,9045,9044,9043,9042,9041,9040,9039,9038,9037,9036,9034,9033,9032,9031,9030,9029,9028,9027,9026,9025,9024,9023,9022,9021,9020,9019,9018,9017,9016,9015,9014,9013,9012,9011,9010,9009,9008,9007,9006,9005,9004,9003,9002,9001,9000,8999,8998,8997,8996,8995,8994,8993,8992,8991,8990,8989,8988,8987,8986,8985,8984,8983,8982,8981,8980,8979,8978,8977,8976,8975,8974,8973,8972,8971,8970,8969,8968,8967,8966,8965,8964,8963,8962,8957,8956,8954,8953,8952,8951,8950,8949,8948,8947,8946,8945,8944,8943,8942,8941,8940,8939,8937,8936,8935,8934,8933,8932,8931,8930,8929,8928,8927,8926,8925,8924,8923,8922,8921,8920,8919,8918,8916,8915,8914,8913,8912,8911,8910,8909,8908,8907,8906,8905,8904,8903,8902,8901,8900,8899,8898,8897,8896,8895,8894,8893,8892,8891,8890,8889,8888,8887,8886,8885,8884,8883,8882,8881,8880,8879,8878,8877,8876,8875,8874,8873,8872,8871,8870,8869,8868,8867,8866,8865,8864,8863,8862,8861,8860,8859,8858,8857,8856,8855,8854,8853,8852,8851,8850,8849,8848,8847,8846,8845,8844,8843,8842,8841,8840,8839,8838,8837,8836,8835,8834,8833,8832,8831,8830,8829,8828,8827,8826,8825,8824,8823,8822,8821,8820,8819,8818,8817,8816,8815,8814,8813,8812,8811,8810,8809,8808,8807,8806,8805,8804,8803,8802,8801,8800,8799,8798,8797,8796,8795,8794,8793,8791,8790,8789,8788,8787,8786,8785,8784,8783,8782,8781,8780,8779,8778,8777,8776,8775,8774,8773,8772,8771,8770,8769,8768,8766,8765,8764,8763,8762,8761,8760,8759,8758,8757,8756,8755,8754,8753,8752,8751,8750,8749,8748,8747,8746,8745,8744,8743,8742,8741,8740,8739,8738,8737,8736,8735,8734,8733,8732,8731,8730,8729,8728,8727,8726,8725,8724,8723,8722,8721,8720,8719,8718,8717,8716,8715,8714,8713,8712,8711,8710,8709,8708,8707,8706,8705,8704,8703,8702,8701,8700,8699,8698,8697,8696,8695,8694,8693,8692,8691,8690,8689,8688,8687,8686,8685,8684,8683,8682,8681,8680,8679,8678,8677,8676,8675,8674,8673,8672,8671,8670,8669,8668,8667,8666,8665,8664,8663,8662,8661,8660,8659,8658,8657,8656,8655,8654,8653,8652,8651,8650,8649,8648,8647,8646,8645,8644,8643,8642,8641,8640,8639,8638,8637,8636,8635,8634,8633,8632,8631,8630,8629,8628,8627,8626,8625,8624,8623,8622,8621,8620,8619,8618,8617,8616,8615,8614,8613,8612,8611,8610,8609,8608,8607,8606,8605,8604,8603,8602,8601,8600,8599,8598,8597,8596,8595,8594,8593,8592,8591,8590,8589,8588,8587,8586,8585,8584,8583,8582,8581,8580,8579,8578,8577,8576,8575,8574,8573,8572,8571,8570,8568,8567,8566,8565,8564,8563,8562,8561,8560,8559,8558,8557,8556,8555,8554,8553,8552,8551,8550,8549,8548,8547,8546,8545,8544,8543,8542,8541,8540,8539,8538,8537,8536,8535,8534,8533,8532,8531,8530,8529,8528,8527,8526,8525,8524,8523,8522,8521,8520,8519,8518,8517,8509,8508,8507,8506,8505,8504,8503,8502,8501,8500,8499,8498,8497,8496,8495,8494,8493,8492,8491,8490,8489,8488,8487,8486,8485,8484,8483,8482,8481,8480,8479,8478,8477,8476,8475,8474,8473,8472,8471,8470,8469,8468,8467,8466,8465,8464,8463,8462,8461,8460,8459,8458,8457,8456,8455,8454,8453,8452,8451,8450,8449,8448,8447,8446,8445,8444,8443,8442,8441,8440,8439,8438,8437,8436,8435,8434,8433,8432,8431,8430,8429,8428,8427,8426,8425,8424,8423,8422,8421,8420,8419,8418,8417,8416,8415,8414,8413,8412,8411,8410,8409,8408,8407,8406,8405,8404,8403,8402,8401,8400,8399,8398,8397,8396,8395,8394,8393,8392,8391,8390,8389,8388,8387,8386,8385,8384,8383,8382,8381,8380,8379,8378,8377,8376,8375,8374,8373,8372,8371,8370,8369,8368,8367,8366,8365,8364,8363,8362,8361,8360,8359,8358,8357,8356,8355,8354,8353,8352,8351,8350,8349,8348,8347,8346,8345,8344,8343,8342,8341,8340,8339,8338,8337,8336,8335,8334,8333,8332,8331,8330,8329,8328,8327,8326,8325,8324,8323,8322,8321,8320,8319,8318,8317,8316,8315,8313,8312,8311,8310,8309,8308";
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
                                    elemLabel.InnerText = issue.Title?.ToString().Replace(",", "");
                                    IssueNode.AppendChild(elemLabel);
                                    rootNode.AppendChild(IssueNode);
                                }

                                if (issue.Body != null)
                                {
                                    XmlElement elemDescription = xmlDoc.CreateElement("Description");
                                    elemDescription.InnerText = issue.Body?.ToString().Replace(",", "");
                                    IssueNode.AppendChild(elemDescription);
                                    rootNode.AppendChild(IssueNode);
                                }

                                if (issue.Title != null && issue.Body != null)
                                {
                                    XmlElement elemDescription = xmlDoc.CreateElement("Title_Description");
                                    elemDescription.InnerText = issue.Title?.ToString() + " " + issue.Body?.ToString().Replace(",", "");
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
                                    elemAssignee.InnerText = issue.Assignee?.Login.ToString();
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
                                        elemClosedAt.InnerText = issue.ClosedAt?.ToString();
                                        IssueNode.AppendChild(elemClosedAt);
                                        rootNode.AppendChild(IssueNode);
                                    }
                                }

                            }
                        }

                    }

                    xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailscli_19112019.xml");
                    MessageBox.Show("Done");
                    #endregion Issues
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailscli_19112019.xml");
            }
        }


        public static async Task<object> GetIssue(string owner, string name, int item)
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

        public static async Task<object> GetPullRequestFiles(string owner, string name, int item)
        {
            try
            {
                return await client.PullRequest.Files(owner, name, item);
            }
            catch (Exception)
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

        public void CounteNoofIssueswithCodes()
        {
            string xmlPath = @"C:\PhD\Workbrench\GitHub_NeturalNetworks\Datasets\IssueDetailsorleans_19112019.xml";
            System.IO.StreamReader xmlStreamReader =
                new System.IO.StreamReader(xmlPath);
            System.Xml.XmlDocument xmlDoc = new System.Xml.XmlDocument();

            xmlDoc.Load(xmlStreamReader);
            xmlStreamReader.Close();
            var rulesDescNodes = xmlDoc.DocumentElement.GetElementsByTagName("IssueDetail");
            int countofcode = 0;
            if (rulesDescNodes != null)
            {                
                foreach (XmlNode node in rulesDescNodes)
                {
                    /* Code From Title_Description*/
                    var _value = node.ChildNodes[3].InnerText;

                    int startIndex = _value.IndexOf("```");
                    int endIndex = _value.LastIndexOf("```");
                    int length = endIndex - startIndex + 1;

                    if (startIndex > -1 && endIndex > -1)
                    {
                        _value = _value.Remove(startIndex, length);
                        node.ChildNodes[3].InnerText = _value;
                        countofcode = countofcode +1;
                    }
                }

                MessageBox.Show("Count : " + countofcode);
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
