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

        private void editFindIssueMenuItem_Click(object sender, EventArgs e) =>
            Search<SearchIssuesBroker>();

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
                        Search(searcher);
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

                    string issuseIDs = "39407,39406,39405,39404,39403,39402,39401,39400,39399,39398,39397,39396,39395,39394,39393,39392,39391,39390,39389,39388,39387,39386,39385,39384,39383,39382,39381,39380,39379,39378,39377,39376,39375,39374,39373,39372,39371,39370,39369,39368,39367,39366,39365,39364,39363,39362,39361,39360,39358,39359,39357,39356,39355,39354,39353,39352,39351,39350,39349,39348,39347,39346,39345,39344,39342,39341,39339,39338,39337,39336,39335,39334,39333,39332,39331,39330,39329,39328,39327,39326,39325,39324,39323,39322,39320,39319,39318,39317,39316,39315,39314,39313,39312,39311,39310,39309,39308,39307,39306,39305,39304,39303,39302,39301,39300,39299,39298,39297,39296,39295,39294,39293,39292,39291,39290,39289,39288,39287,39286,39321,39284,39283,39282,39281,39280,39279,39278,39277,39276,39275,39274,39273,39272,39271,39270,39269,39268,39267,39266,39265,39264,39263,39262,39261,39260,39259,39258,39257,39256,39255,39254,39253,39252,39251,39250,39249,39248,39247,39246,39245,39244,39243,39242,39241,39240,39239,39238,39237,39236,39235,39234,39233,39232,39231,39230,39229,39228,39227,39226,39225,39224,39223,39222,39221,39220,39219,39218,39217,39216,39215,39214,39213,39212,39211,39210,39209,39208,39207,39206,39205,39204,39203,39202,39201,39200,39199,39198,39197,39196,39195,39194,39193,39192,39191,39190,39189,39188,39187,39186,39185,39184,39183,39182,39181,39180,39179,39178,39177,39176,39175,39174,39173,39172,39171,39170,39169,39168,39167,39166,39165,39164,39163,39162,39161,39160,39159,39158,39157,39156,39155,39154,39153,39152,39151,39150,39148,39147,39146,39145,39144,39143,39142,39141,39140,39139,39138,39137,39136,39135,39134,39133,39132,39149,39130,39129,39128,39127,39126,39125,39124,39123,39122,39121,39120,39119,39118,39117,39116,39115,39114,39113,39112,39111,39110,39109,39108,39107,39106,39105,39104,39103,39102,39101,39100,39098,39097,39096,39095,39094,39093,39092,39091,39090,39089,39088,39087,39086,39085,39084,39083,39082,39081,39080,39079,39078,39077,39076,39075,39074,39073,39072,39071,39070,39069,39068,39067,39066,39065,39064,39063,39062,39061,39060,39059,39058,39057,39056,39055,39054,39053,39052,39051,39050,39049,39048,39047,39046,39043,39042,39041,39040,39039,39038,39037,39036,39035,39034,39033,39031,39030,39029,39028,39027,39026,39025,39024,39023,39022,39021,39020,39019,39018,39017,39016,39015,39014,39013,39012,39011,39010,39009,39008,39007,39006,39005,39004,39003,39002,39001,39000,38999,38998,38997,38996,38995,38994,38993,38992,38991,38990,38989,38988,38987,38986,38985,38984,38983,38981,38980,38979,38982,38978,38977,38976,38975,38974,38973,38972,38971,38970,38969,38968,38967,38966,38964,38963,38962,38961,38960,38959,38958,38957,38956,38955,38954,38953,38952,38951,38950,38949,38948,38947,38946,38945,38944,38942,38941,39032,38939,38938,38937,38936,38935,38934,38932,38931,38930,38929,38928,38927,38925,38924,38923,38922,38921,38920,38919,38918,38917,38916,38915,38914,38912,38911,38910,38909,38908,38907,38906,38905,38904,38903,38902,38901,38900,38899,38898,38897,38896,38895,38894,38893,38892,38891,38890,38889,38888,38887,38886,38885,38884,38883,38882,38881,38880,38879,38878,38877,38876,38875,38874,38873,38872,38871,38870,38869,38868,38867,38866,38865,38864,38863,38862,38861,38860,38859,38858,38857,38856,38855,38854,38853,38852,38851,38850,38849,38848,38847,38846,38845,38844,38843,38842,38841,38840,38839,38838,38837,38836,38835,38834,38833,38832,38831,38830,38829,38828,38827,38826,38825,38824,38823,38822,38821,38820,38819,38818,38817,38816,38815,38814,38813,38812,38811,38810,38809,38808,38807,38806,38805,38804,38803,38802,38801,38800,38799,38798,38797,38796,38795,38794,38793,38792,38791,38790,38789,38788,38787,38786,38785,38784,38783,38782,38781,38780,38779,38778,38777,38776,38775,38774,38773,38772,38771,38770,38769,38768,38767,38766,38765,38764,38763,38762,38761,38760,38759,38758,38757,38756,38755,38754,38753,38752,38751,38750,38749,38748,38747,38746,38745,38744,38743,38742,38741,38740,38739,38738,38737,38736,38735,38734,38733,38732,38731,38730,38729,38728,38727,38726,38725,38724,38723,38722,38721,38720,38719,38718,38717,38716,38715,38714,38713,38712,38711,38710,38709,38708,38707,38706,38705,38704,38703,38702,38701,38700,38699,38698,38697,38696,38695,38694,38693,38692,38691,38690,38689,38688,38687,38686,38685,38684,38683,38682,38681,38680,38678,38677,38676,38675,38674,38673,38672,38671,38670,38669,38668,38667,38666,38665,38664,38663,38662,38661,38660,38659,38658,38657,38656,38655,38654,38653,38652,38651,38650,38649,38648,38647,38646,38645,38644,38643,38642,38641,38640,38639,38638,38637,38636,38679,38635,38634,38633,38632,38631,38630,38629,38628,38627,38626,38625,38624,38623,38622,38621,38620,38619,38618,38617,38616,38615,38614,38613,38612,38611,38610,38609,38608,38607,38606,38605,38604,38603,38602,38601,38600,38599,38598,38597,38596,38595,38594,38593,38592,38591,38590,38589,38588,38587,38586,38585,38584,38583,38582,38581,38579,38578,38577,38576,38575,38574,38573,38572,38571,38570,38569,38568,38567,38566,38565,38564,38563,38562,38561,38560,38559,38558,38557,38556,38555,38554,38553,38552,38551,38550,38549,38548,38547,38546,38545,38544,38543,38542,38541,38540,38539,38538,38537,38535,38534,38533,38532,38531,38530,38529,38528,38527,38526,38525,38524,38523,38522,38521,38518,38517,38516,38515,38514,38513,38512,38511,38510,38509,38508,38507,38506,38505,38504,38503,38502,38501,38500,38499,38498,38497,38496,38495,38494,38493,38492,38491,38490,38489,38488,38487,38486,38485,38484,38483,38482,38481,38480,38479,38478,38477,38476,38475,38474,38473,38472,38471,38470,38469,38468,38467,38466,38465,38464,38463,38462,38461,38460,38459,38457,38456,38455,38454,38453,38451,38450,38449,38448,38447,38446,38445,38444,38443,38442,38441,38440,38439,38438,38436,38435,38434,38433,38432,38431,38430,38429,38428,38427,38426,38425,38424,38423,38422,38421,38420,38419,38418,38417,38416,38415,38414,38413,38412,38411,38410,38409,38408,38407,38406,38405,38404,38403,38402,38401,38400,38399,38398,38397,38396,38395,38394,38393,38392,38391,38389,38388,38387";

                    List<string> issueslists = new List<string>();
                    issueslists = issuseIDs.ToString().Split(',').ToList();

                    #region Issues 

                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.Load("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsRoslyn_20102019.xml");
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
                                    elemLabel.InnerText = String.Join("|", issue.Labels.Select(x => x.Name).ToList());
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
                                                var potentialAssignees = issueevents?.Where(x => collaborators.Contains(x.Actor.Login.ToString())).Select(x=> x.Actor.Login.ToString()).Distinct();
                                                if (potentialAssignees != null)
                                                {
                                                    XmlElement elemAssignee = xmlDoc.CreateElement("Assignee");
                                                    elemAssignee.InnerText = String.Join("|", potentialAssignees.Select(x => x).ToList());
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

                    xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\IssueDetailsRoslyn_20102019.xml");
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
