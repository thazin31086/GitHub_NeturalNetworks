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
                      //  GetIssueDetails();
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
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsRoslyn_02112019_3.xml");
            XmlNode rootNode = xmlDoc["IssueDetails"];
            try
            {
                var Contributors = GetContributors(owner, name);
                if (!Contributors.IsCompleted)
                {
                    var collaboratorresponse = await Contributors.ConfigureAwait(false);
                    IReadOnlyList<RepositoryContributor> _collaboratorresponse = collaboratorresponse as IReadOnlyList<RepositoryContributor>;
                    List<string> collaborators = _collaboratorresponse?.Select(x => x.Login).ToList();
                    
                    string issuedIDs = "35751,35750,35749,35748,35747,35746,35745,35744,35743,35742,35741,35740,35739,35738,35737,35736,35735,35734,35733,35732,35731,35730,35729,35727,35726,35725,35724,35723,35722,35721,35720,35719,35718,35717,35716,35715,35714,35713,35712,35711,35710,35709,35708,35707,35706,35705,35704,35703,35702,35701,35700,35699,35698,35697,35696,35695,35694,35728,35693,35692,35691,35690,35689,35688,35687,35686,35685,35684,35683,35681,35680,35679,35678,35677,35676,35675,35674,35673,35672,35671,35670,35669,35668,35667,35666,35665,35664,35663,35662,35661,35660,35659,35658,35657,35656,35655,35654,35653,35652,35651,35650,35649,35648,35647,35646,35645,35644,35643,35642,35641,35640,35639,35638,35637,35636,35635,35634,35633,35632,35631,35630,35629,35628,35627,35626,35625,35624,35623,35622,35621,35620,35619,35618,35617,35616,35615,35614,35613,35612,35611,35610,35609,35608,35607,35606,35605,35604,35603,35602,35601,35600,35599,35598,35597,35596,35595,35594,35593,35592,35591,35590,35589,35588,35587,35586,35585,35584,35583,35582,35581,35580,35579,35578,35577,35576,35575,35574,35573,35572,35571,35570,35569,35568,35567,35566,35565,35564,35563,35562,35561,35560,35559,35558,35557,35556,35555,35554,35553,35552,35550,35549,35548,35547,35546,35545,35544,35543,35542,35541,35540,35539,35538,35537,35536,35535,35534,35533,35532,35531,35530,35529,35528,35527,35526,35525,35524,35523,35522,35521,35520,35519,35518,35517,35516,35515,35514,35513,35512,35511,35510,35509,35508,35507,35506,35505,35504,35503,35502,35501,35500,35499,35498,35497,35496,35495,35494,35493,35492,35491,35490,35489,35488,35487,35486,35485,35483,35482,35481,35480,35479,35478,35477,35476,35475,35474,35473,35472,35471,35470,35469,35468,35467,35466,35465,35464,35463,35462,35461,35460,35459,35458,35457,35456,35455,35454,35453,35452,35451,35450,35449,35448,35447,35446,35445,35444,35443,35442,35441,35440,35439,35438,35437,35436,35435,35434,35433,35432,35431,35430,35429,35428,35427,35426,35425,35424,35423,35422,35421,35420,35419,35418,35417,35416,35415,35414,35413,35412,35411,35410,35409,35408,35407,35406,35405,35404,35403,35402,35401,35400,35399,35398,35397,35396,35395,35394,35393,35392,35391,35390,35389,35388,35387,35386,35385,35384,35383,35382,36896,36895,36894,36893,36892,36891,36890,36889,36888,36887,36886,36885,36884,36883,36882,36881,36880,36879,36878,36877,36876,36875,36874,36873,36872,36871,36870,36869,36868,36867,36866,36865,36864,36863,36862,36861,36860,36859,36858,36857,36856,36855,36854,36853,36852,36851,36850,36849,36848,36847,36846,36845,36844,36843,36842,36841,36840,36839,36838,36836,36835,36834,36833,36832,36831,36830,36829,36828,36827,36826,36825,36824,36823,36822,36821,36820,36819,36818,36817,36816,36815,36814,36813,36812,36811,36810,36809,36808,36807,36806,36805,36804,36803,36802,36801,36800,36799,36798,36797,36796,36795,36794,36793,36792,36791,36790,36789,36788,36787,36786,36785,36784,36783,36782,36781,36779,36778,36777,36776,36775,36774,36773,36771,36770,36769,36768,36767,36766,36765,36764,36763,36762,36761,36760,36759,36758,36757,36756,36755,36754,36753,36752,36751,36750,36749,36748,36747,36746,36745,36744,36743,36742,36741,36740,36739,36738,36737,36736,36735,36734,36733,36732,36731,36730,36729,36728,36727,36726,36725,36724,36723,36722,36721,36720,36719,36718,36717,36715,36716,36714,36713,36712,36711,36710,36709,36708,36707,36706,36705,36704,36703,36702,36701,36700,36699,36698,36697,36696,36695,36694,36693,36692,36691,36690,36689,36688,36687,36686,36685,36684,36683,36682,36681,36680,36679,36678,36677,36676,36674,36673,36672,36671,36670,36669,36668,36667,36666,36665,36664,36663,36662,36661,36660,36659,36658,36657,36656,36655,36654,36653,36652,36651,36650,36649,36648,36647,36646,36645,36644,36675,36643,36642,36641,36640,36639,36638,36637,36636,36635,36634,36633,36632,36631,36630,36629,36628,36627,36626,36625,36624,36623,36622,36621,36620,36619,36618,36617,36616,36615,36614,36613,36612,36611,36610,36609,36608,36607,36606,36605,36604,36603,36602,36601,36600,36599,36598,36597,36596,36595,36594,36592,36591,36590,36589,36588,36587,36586,36585,36584,36583,36582,36581,36580,36579,36578,36577,36576,36575,36574,36573,36572,36570,36569,36568,36567,36566,36565,36564,36563,36562,36561,36560,36559,36558,36557,36556,36555,36554,36553,36552,36551,36550,36549,36548,36547,36546,36545,36544,36543,36542,36541,36540,36539,36538,36537,36535,36534,36533,36532,36531,36530,36529,36528,36527,36526,36525,36524,36523,36522,36521,36520,36519,36518,36517,36516,36515,36514,36513,36512,36511,36510,36509,36508,36507,36506,36505,36504,36503,36502,36501,36500,36499,36498,36497,36496,36495,36494,36493,36492,36491,36490,36489,36488,36487,36486,36485,36484,36483,36482,36481,36480,36479,36478,36477,36476,36475,36474,36473,36472,36471,36470,36469,36468,36467,36466,36465,36464,36463,36462,36461,36460,36459,36458,36457,36456,36455,36454,36453,36452,36451,36450,36449,36448,36447,36446,36445,36444,36443,36442,36441,36440,36439,36438,36437,36436,36435,36434,36433,36432,36431,36430,36429,36428,36427,36426,36425,36424,36423,36422,36421,36420,36419,36418,36417,36416,36415,36414,36413,36412,36411,36410,36409,36408,36407,36406,36405,36404,36403,36402,36401,36400,36399,36398,36397,36396,36395,36394,36393,36392,36391,36390,36389,36388,36387,36386,36385,36384,36383,36382,36381,36380,36379,36378,36377,36376,36375,36374,36373,36372,36371,36370,36369,36368,36367,36366,36365,36364,36363,36362,36361,36360,36359,36358,36357,36356,36355,36354,36353,36352,36351,36350,36349,36348,36347,36346,36345,36344,36343,36342,36341,36340,36339,36338,36337,36336,36335,36334,36333,36332,36331,36330,36329,36328,36327,36326,36325,36324,36323,36322,36321,36320,36319,36318,36317,36316,36315,36314,36313,36312,36311,36310,36309,36308,36307,36306,36305,36304,36303,36302,36301,36300,36299,36298,36297,36296,36295,36294,36293,36292,36291,36290,36289,36288,36287,36286,36285,36284,36283,36282,36281,36280,36279,36278,36277,36276,36275,36274,36273,36272,36271,36270,36269,36268,36267,36266,36265,36264,36263,36262,36261,36260,36259,36258,36257,36256,36255,36254,36253,36252,36251,36250,36249,36248,36247,36246,36245,36244,36243,36242,36241,36240,36239,36238,36237,36236,36235,36234,36233,36232,36231,36230,36229,36228,36227,36226,36225,36224,36223,36222,36221,36220,36219,36218,36217,36216,36215,36214,36213,36211,36210,36209,36208,36207,36206,36205,36204,36203,36202,36201,36200,36199,36198,36197,36196,36195,36194,36193,36192,36191,36190,36189,36188,36187,36186,36185,36184,36183,36182,36181,36180,36179,36178,36177,36176,36175,36174,36173,36172,36171,36170,36169,36168,36167,36166,36165,36164,36163,36162,36161,36160,36159,36158,36157,36156,36154,36153,36152,36151,36150,36149,36148,36147,36146,36145,36144,36143,36142,36141,36140,36139,36138,36137,36136,36135,36134,36133,36132,36131,36130,36129,36128,36127,36126,36125,36124,36123,36122,36121,36120,36119,36118,36117,36116,36115,36114,36113,36112,36111,36110,36109,36108,36107,37641,37640,37639,37638,37637,37636,37635,37634,37633,37632,37631,37630,37629,37628,37627,37626,37625,37624,37623,37622,37620,37619,37618,37617,37616,37615,37614,37613,37612,37611,37610,37609,37608,37607,37606,37605,37604,37602,37601,37600,37599,37598,37597,37596,37595,37594,37592,37591,37590,37589,37588,37587,37586,37585,37593,37584,37583,37582,37581,37580,37579,37578,37577,37576,37575,37574,37573,37572,37571,37570,37569,37568,37621,37567,37566,37565,37564,37563,37562,37560,37559,37558,37557,37556,37555,37554,37552,37551,37550,37549,37548,37547,37546,37545,37544,37543,37542,37541,37540,37539,37538,37537,37536,37535,37534,37533,37532,37531,37530,37529,37528,37527,37526,37525,37524,37523,37522,37521,37520,37519,37518,37517,37516,37515,37514,37513,37512,37511,37510,37509,37508,37507,37506,37505,37503,37502,37501,37500,37499,37497,37496,37495,37494,37493,37492,37491,37490,37489,37488,37487,37486,37485,37484,37483,37482,37481,37480,37479,37478,37477,37476,37475,37473,37472,37471,37470,37469,37468,37467,37466,37465,37464,37463,37462,37461,37460,37459,37458,37456,37455,37454,37457,37453,37452,37451,37450,37449,37448,37447,37446,37445,37444,37443,37442,37441,37440,37439,37438,37437,37436,37435,37434,37433,37432,37431,37430,37429,37428,37427,37426,37425,37424,37423,37422,37421,37420,37419,37417,37416,37415,37414,37413,37412,37411,37410,37409,37408,37407,37406,37405,37404,37403,37402,37401,37400,37398,37397,37396,38121,37395,37394,37393,37392,37391,37390,37389,37388,37387,37386,37385,37384,37383,37382,37381,37380,37379,37378,37377,37376,37375,37374,37373,37371,37370,37369,37368,37367,37366,37365,37364,37363,37362,37361,37360,37359,37358,37357,37356,37355,37354,37353,37352,37351,37350,37349,37348,37347,37346,37345,37344,37343,37342,37341,37340,37339,37338,37337,37336,37335,37334,37333,37332,37331,37330,37329,37328,37327,37326,37325,37324,37323,37322,37321,37320,37319,37318,37317,37316,37315,37314,37313,37312,37311,37310,37309,37308,37307,37306,37305,37304,37303,37302,37301,37300,37299,37298,37297,37296,37295,37294,37293,37292,37291,37290,37289,37288,37287,37286,37285,37284,37283,37282,37281,37280,37279,37278,37277,37276,37275,37274,37273,37272,37271,37270,37269,37268,37267,37266,37265,37264,37263,37262,37261,37260,37258,37257,37256,37255,37254,37253,37252,37251,37250,37249,37248,37247,37245,37244,37243,37242,37241,37240,37239,37238,37237,37236,37235,37234,37233,37232,37231,37230,37229,37228,37227,37226,37225,37224,37223,37222,37221,37220,37219,37218,37217,37216,37215,37214,37213,37212,37211,37246,37210,37209,37208,37207,37206,37205,37204,37203,37202,37201,37200,37199,37198,37197,37196,37195,37194,37193,37192,37191,37190,37189,37188,37187,37186,37185,37184,37183,37182,37181,37180,37179,37178,37177,37176,37175,37174,37173,37172,37171,37170,37169,37168,37167,37166,37165,37164,37163,37162,37161,37160,37159,37158,37157,37156,37155,37154,37153,37152,37151,37150,37149,37148,37147,37146,37145,37144,37143,37142,37141,37140,37139,37138,37137,37136,37135,37134,37133,37132,37131,37130,37129,37128,37127,37126,37125,37124,37123,37122,37121,37120,37119,37118,37117,37116,37115,37114,37113,37112,37111,37110,37109,37108,37107,37106,37105,37104,37103,37102,37101,37100,37099,37098,37097,37096,37095,37094,37093,37092,37091,37090,37089,37088,37087,37086,37085,37084,37083,37082,37081,37079,37078,37077,37076,37075,37074,37073,37072,37071,37070,37069,37068,37067,37066,37065,37064,37063,37062,37061,37060,37059,37058,37057,37056,37055,37054,37053,37052,37051,37050,37049,37048,37047,37046,37045,37044,37043,37042,37041,37040,37039,37038,37037,37036,37035,37034,37033,37032,37031,37030,37029,37028,37027,37026,37025,37024,37023,37022,37021,37020,37019,37018,37017,37016,37015,37014,37013,37012,37011,37010,37009,37008,37007,37006,37005,37004,37003,37002,37001,37000,36999,36998,36997,36996,36995,36994,36993,36992,36991,36990,36989,36988,36987,36986,36985,36984,36983,36982,36981,36980,36979,36978,36977,36976,36975,36974,36973,36972,36971,36970,36969,36968,36967,36966,36965,36964,36963,36962,36961,36960,36959,36958,36957,36956,36955,36954,36953,36952,36951,36950,36949,36948,36947,36946,36945,36944,36943,36942,36941,36940,36939,36938,36937,36936,36935,36934,36933,36932,36931,36930,36929,36928,36927,36926,36925,36924,36923,36922,36921,36920,36919,36918,36917,36916,36915,36914,36913,36912,36911,36910,36909,36908,36907,36906,36905,36904,36903,36901,36900,36899,36898,36897,38440,38439,38438,38436,38435,38434,38433,38432,38431,38430,38429,38428,38427,38426,38425,38424,38423,38422,38421,38420,38419,38418,38417,38416,38415,38414,38413,38412,38411,38410,38409,38408,38407,38406,38405,38404,38403,38402,38401,38400,38399,38398,38397,38396,38395,38394,38393,38392,38391,38389,38388,38387,38386,38385,38384,38383,38382,38381,38380,38379,38378,38377,38376,38375,38374,38373,38372,38371,38370,38369,38368,38366,38365,38364,38363,38362,38361,38360,38359,38358,38357,38356,38355,38354,38353,38352,38351,38350,38349,38348,38347,38346,38345,38344,38343,38342,38341,38339,38338,38337,38336,38335,38334,38333,38332,38331,38330,38329,38328,38327,38326,38325,38324,38323,38322,38321,38320,38319,38318,38317,38316,38315,38314,38313,38312,38311,38310,38309,38308,38307,38306,38305,38304,38303,38302,38301,38300,38299,38298,38297,38296,38293,38291,38290,38289,38288,38287,38286,38285,38284,38283,38282,38281,38280,38279,38278,38277,38276,38275,38273,38272,38271,38270,38269,38268,38267,38266,38265,38264,38263,38262,38261,38260,38259,38258,38257,38256,38255,38254,38253,38252,38251,38250,38249,38248,38247,38246,38245,38244,38243,38242,38241,38240,38239,38238,38236,38235,38234,38233,38232,38231,38230,38229,38228,38227,38226,38225,38224,38223,38222,38221,38220,38219,38218,38217,38216,38215,38214,38213,38212,38211,38210,38208,38207,38206,38205,38204,38203,38202,38201,38200,38199,38198,38197,38196,38195,38194,38193,38192,38191,38190,38189,38188,38187,38186,38185,38184,38183,38182,38181,38180,38179,38178,38177,38176,38175,38174,38173,38172,38171,38170,38169,38168,38167,38166,38165,38164,38163,38162,38161,38160,38159,38158,38157,38156,38155,38154,38153,38152,38151,38150,38149,38148,38147,38146,38145,38144,38143,38142,38141,38140,38139,38138,38137,38136,38135,38134,38133,38132,38131,38130,38129,38128,38127,38126,38125,38124,38122,38120,38119,38118,38117,38116,38115,38114,38113,38112,38111,38110,38109,38108,38107,38106,38105,38104,38103,38102,38101,38100,38099,38098,38097,38096,38095,38094,38093,38092,38091,38090,38089,38087,38086,38085,38084,38083,38082,38081,38080,38079,38078,38077,38074,38072,38071,38070,38069,38068,38067,38066,38065,38064,38063,38062,38061,38060,38059,38058,38057,38056,38055,38054,38053,38052,38050,38051,38049,38048,38047,38046,38045,38044,38043,38042,38041,38040,38039,38038,38037,38036,38035,38034,38033,38032,38031,38030,38029,38028,38027,38026,38025,38024,38023,38022,38021,38020,38019,38018,38017,38016,38015,38014,38013,38012,38011,38010,38009,38008,38007,38006,38005,38004,38003,38002,38001,38000,37999,37998,37997,37996,37995,37994,37993,37992,37991,37990,37989,37988,37987,37986,37985,37984,37983,37981,37980,37979,37978,37977,37976,37975,37974,37973,37972,37971,37970,37969,37968,37967,37966,37965,37964,37963,37962,37961,37960,37959,37958,37957,37956,37955,37954,37953,37952,37951,37950,37949,37948,37947,37946,37945,37944,37943,37942,37941,37940,37939,37938,37937,37936,37935,37934,37933,37932,37931,37930,37929,37928,37927,37926,37925,37924,37923,37922,37921,37920,37918,37917,37916,37915,37914,37913,37912,37911,37910,37909,37908,37907,37906,37905,37904,37903,37902,37901,37900,37899,37898,37897,37896,37895,37894,37893,37892,37891,37889,37888,37887,37886,37885,37884,37883,37882,37881,37880,37879,37878,37877,37876,37875,37874,37873,37919,37872,37871,37870,37869,37868,37867,37866,37865,37864,37863,37862,37861,37860,37859,37857,37856,37855,37854,37853,37852,37851,37849,37848,37847,37846,37845,37844,37843,37842,37841,37840,37839,37838,37837,37836,37835,37834,37833,37832,37831,37830,37829,37828,37827,37826,37825,37824,37823,37822,37821,37820,37819,37818,37817,37816,37815,37814,37813,37812,37811,37810,37809,37808,37807,37806,37805,37804,37803,37802,37801,37800,37799,37798,37797,37796,37795,37794,37793,37792,37791,37790,37789,37788,37787,37786,37785,37784,37783,37782,37781,37780,37779,37778,37777,37776,37775,37774,37773,37772,37771,37770,37769,37768,37767,37766,37765,37764,37763,37762,37761,37760,37759,37758,37757,37756,37755,37754,37753,37752,37751,37750,37749,37748,37747,37746,37745,37744,37743,37742,37741,37740,37738,37739,37737,37736,37735,37734,37733,37732,37731,37730,37729,37728,37727,37726,37725,37724,37723,37722,37721,37720,37719,37718,37717,37716,37715,37714,37713,37712,37711,37710,37709,37708,37707,37706,37705,37704,37703,37702,37701,37700,37699,37698,37697,37696,37695,37694,37693,37692,37691,37690,37689,37688,37687,37686,37685,37684,37683,37682,37681,37680,37679,37678,37677,37676,37675,37674,37673,37672,37671,37670,37669,37668,37667,37666,37665,37664,37663,37662,37661,37660,37659,37658,37657,37656,37655,37654,37653,37652,37651,37650,37649,37648,37647,37646,37645,37644,37643,37642,38971,38970,38969,38968,38967,38966,38964,38963,38962,38961,38960,38959,38958,38957,38956,38955,38954,38953,38952,38951,38950,38949,38948,38947,38946,38945,38944,38942,38941,39032,38939,38938,38937,38936,38935,38934,38932,38931,38930,38929,38928,38927,38925,38924,38923,38922,38921,38920,38919,38918,38917,38916,38915,38914,38912,38911,38909,38908,38907,38906,38905,38904,38903,38902,38901,38900,38899,38898,38897,38896,38895,38894,38893,38892,38891,38890,38889,38888,38887,38886,38885,38884,38883,38882,38881,38880,38879,38878,38877,38876,38875,38874,38873,38872,38871,38870,38869,38868,38867,38866,38865,38864,38863,38862,38861,38860,38859,38858,38857,38856,38855,38854,38853,38852,38851,38850,38849,38848,38847,38846,38845,38844,38843,38842,38841,38840,38839,38838,38837,38836,38835,38834,38833,38832,38831,38830,38829,38828,38827,38826,38825,38824,38823,38822,38821,38820,38819,38818,38817,38816,38815,38814,38813,38812,38811,38810,38809,38808,38807,38806,38805,38804,38803,38802,38801,38800,38799,38798,38797,38796,38795,38794,38793,38792,38791,38790,38789,38788,38787,38786,38785,38784,38783,38782,38781,38780,38779,38778,38777,38776,38775,38774,38773,38772,38771,38770,38769,38768,38767,38766,38765,38764,38763,38762,38761,38760,38759,38758,38757,38756,38755,38754,38753,38752,38751,38750,38749,38748,38747,38746,38745,38744,38743,38742,38741,38740,38739,38738,38737,38736,38735,38734,38733,38732,38731,38730,38729,38728,38727,38726,38725,38724,38723,38722,38721,38720,38719,38718,38717,38716,38715,38714,38713,38712,38711,38710,38709,38708,38707,38706,38705,38704,38703,38702,38701,38700,38699,38698,38697,38696,38695,38694,38693,38692,38691,38690,38689,38688,38687,38686,38685,38684,38683,38682,38681,38680,38678,38677,38676,38675,38674,38673,38672,38671,38670,38669,38668,38667,38666,38665,38664,38663,38662,38661,38660,38659,38658,38657,38656,38655,38654,38653,38652,38651,38650,38649,38648,38647,38646,38645,38644,38643,38642,38641,38640,38639,38638,38637,38636,38679,38635,38634,38633,38632,38631,38630,38629,38628,38627,38626,38625,38624,38623,38622,38621,38620,38619,38618,38617,38616,38615,38614,38613,38612,38611,38610,38609,38608,38607,38606,38605,38604,38603,38602,38601,38600,38599,38598,38597,38596,38595,38594,38593,38592,38591,38590,38589,38588,38587,38586,38585,38584,38583,38582,38581,38579,38578,38577,38576,38575,38574,38573,38572,38571,38570,38569,38568,38567,38566,38565,38564,38563,38562,38561,38560,38559,38558,38557,38556,38555,38554,38553,38552,38551,38550,38549,38548,38547,38546,38545,38544,38543,38542,38541,38540,38539,38538,38537,38535,38534,38533,38532,38531,38530,38529,38528,38527,38526,38525,38524,38523,38522,38521,38518,38517,38516,38515,38514,38513,38512,38511,38510,38509,38508,38507,38506,38505,38504,38503,38502,38501,38500,38499,38498,38497,38496,38495,38494,38493,38492,38491,38490,38489,38488,38487,38486,38485,38484,38483,38482,38481,38480,38479,38478,38477,38476,38475,38474,38473,38472,38471,38470,38469,38468,38467,38466,38465,38464,38463,38462,38461,38460,38459,38457,38456,38455,38454,38453,38451,38450,38449,38448,38447,38446,38445,38444,38443,38442,38441,39620,39619,39618,39617,39616,39615,39614,39613,39612,39611,39610,39609,39608,39607,39606,39605,39604,39603,39602,39601,39600,39599,39598,39597,39596,39595,39594,39593,39592,39591,39590,39589,39588,39587,39586,39585,39584,39582,39581,39580,39583,39579,39578,39577,39576,39575,39574,39573,39572,39571,39570,39569,39568,39567,39566,39565,39564,39563,39562,39561,39560,39559,39558,39557,39556,39555,39554,39553,39552,39551,39550,39549,39548,39547,39546,39545,39544,39543,39542,39541,39540,39539,39538,39537,39536,39535,39534,39533,39532,39531,39530,39529,39528,39527,39526,39525,39524,39523,39522,39520,39519,39518,39517,39516,39515,39514,39513,39512,39511,39510,39509,39508,39507,39506,39505,39504,39503,39502,39501,39500,39499,39498,39496,39495,39494,39493,39492,39491,39490,39489,39488,39487,39486,39485,39484,39483,39482,39481,39480,39479,39477,39476,39475,39474,39473,39472,39471,39469,39497,39468,39467,39466,39465,39464,39463,39462,39461,39460,39459,39458,39457,39456,39455,39454,39453,39452,39451,39450,39449,39448,39447,39446,39445,39444,39443,39442,39441,39440,39439,39438,39437,39436,39435,39434,39433,39432,39431,39430,39429,39428,39427,39426,39425,39423,39422,39421,39420,39419,39418,39417,39416,39415,39414,39413,39412,39411,39410,39409,39408,39407,39406,39405,39404,39403,39402,39401,39400,39399,39398,39397,39396,39395,39394,39393,39392,39391,39390,39389,39388,39387,39386,39385,39384,39383,39382,39381,39380,39470,39379,39378,39377,39376,39375,39374,39373,39372,39371,39370,39369,39368,39367,39366,39365,39364,39363,39362,39361,39360,39358,39359,39357,39356,39355,39354,39352,39351,39350,39349,39348,39347,39346,39345,39344,39342,39341,39339,39338,39337,39336,39335,39334,39333,39332,39331,39330,39329,39328,39327,39326,39325,39324,39323,39322,39320,39319,39318,39317,39316,39315,39314,39313,39312,39311,39310,39309,39308,39307,39306,39305,39304,39303,39302,39301,39300,39299,39298,39297,39296,39295,39294,39293,39292,39291,39290,39289,39288,39287,39286,39321,39284,39283,39282,39281,39280,39279,39278,39277,39276,39275,39274,39273,39272,39271,39270,39269,39268,39267,39266,39265,39264,39263,39262,39424,39261,39260,39259,39258,39257,39256,39255,39254,39253,39252,39251,39250,39249,39248,39247,39246,39245,39244,39243,39242,39241,39240,39239,39238,39237,39236,39235,39234,39233,39232,39231,39230,39229,39228,39227,39226,39225,39224,39223,39222,39221,39220,39219,39218,39217,39216,39215,39214,39213,39212,39211,39210,39209,39208,39207,39206,39205,39204,39203,39202,39201,39200,39199,39198,39197,39196,39195,39194,39193,39192,39191,39190,39189,39188,39187,39186,39185,39184,39183,39182,39181,39180,39179,39178,39177,39176,39175,39174,39173,39172,39171,39170,39169,39168,39167,39166,39165,39164,39163,39162,39161,39160,39159,39158,39157,39156,39155,39154,39153,39152,39151,39150,39148,39147,39146,39145,39144,39143,39142,39141,39140,39139,39138,39137,39136,39135,39134,39133,39132,39149,39130,39129,39128,39127,39126,39125,39124,39123,39122,39121,39120,39119,39118,39117,39116,39115,39114,39113,39112,39111,39110,39109,39108,39107,39106,39105,39104,39103,39102,39101,39100,39098,39097,39096,39095,39094,39093,39092,39091,39090,39089,39088,39087,39086,39085,39084,39083,39082,39081,39080,39079,39078,39077,39076,39075,39074,39073,39072,39071,39070,39069,39068,39067,39066,39065,39064,39063,39062,39061,39060,39059,39058,39057,39056,39055,39054,39053,39052,39051,39050,39049,39048,39047,39046,39043,39042,39041,39040,39039,39038,39037,39036,39035,39034,39033,39031,39030,39029,39028,39027,39026,39025,39024,39023,39022,39021,39020,39019,39018,39017,39016,39015,39014,39013,39012,39011,39010,39009,39008,39007,39006,39005,39004,39003,39002,39001,39000,38999,38998,38997,38996,38995,38994,38993,38992,38991,38990,38989,38988,38987,38986,38985,38984,38983,38981,38980,38979,38982,38978,38977,38976,38975,38974,38973,38972";
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

                    xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsRoslyn_02112019_3.xml");
                    MessageBox.Show("Done");
                    #endregion Issues
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsRoslyn_02112019_3.xml");
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
