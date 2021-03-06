﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Octokit;

namespace GitHubApiDemo
{

    /// <summary>
    /// An abstraction to get loosely typed search results, using one of the GitHub API
    /// search methods.
    /// </summary>
    public abstract class Searcher
    {
        #region Constructors
        /// <summary>
        /// Create a searcher for the specified client, limited to the specified number of items.
        /// </summary>
        /// <param name="client">The GitHub client.</param>
        /// <param name="maximumCount">The maximum number of items to return.</param>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="client"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// <paramref name="maximumCount"/> is less than or equal to zero.
        /// </exception>
        protected Searcher(GitHubClient client, int maximumCount,
            BackgroundWorker backgroundWorker = null)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            MaximumCount = maximumCount > 0 ? maximumCount :
                throw new ArgumentOutOfRangeException(nameof(maximumCount));
            BackgroundWorker = backgroundWorker;
        }
        #endregion // Constructors

        #region Constants
        /// <summary>
        /// The number of levels of properties to display as available.
        /// </summary>
        public const int Depth = 3;
        #endregion // Constants

        #region Private data
        private int isBusy;
        #endregion // Private data

        #region Properties
        /// <summary>
        /// Gets the background worker (if any) associated with this searcher.
        /// </summary>
        public BackgroundWorker BackgroundWorker { get; }

        /// <summary>
        /// Gets a value indicating if this search can get additional detail.
        /// </summary>
        public virtual bool CanGetDetail => false;

        /// <summary>
        /// Gets the GitHub client for this searcher.
        /// </summary>
        public GitHubClient Client { get; }

        /// <summary>
        /// Gets or sets the selected column names for this type of searcher.
        /// </summary>
        public abstract ColumnSet Columns { get; set; }

        /// <summary>
        /// Gets the current number of results obtained for the most recent search.
        /// </summary>
        public int? Count { get; protected set; }

        /// <summary>
        /// Gets a value indicating if this search is busy.
        /// </summary>
        public bool IsBusy =>
            isBusy != 0;

        /// <summary>
        /// Gets a value indicating if the most recent search was cancelled.
        /// </summary>
        public bool IsCancelled { get; protected set; }

        /// <summary>
        /// Gets the maximum number of items returned by this searcher.
        /// </summary>
        public int MaximumCount { get; }

        /// <summary>
        /// Gets the total number of results for the most recent search.
        /// </summary>
        public int? TotalCount { get; protected set; }

        /// <summary>
        /// Gets the type of entity that is the target of this search.
        /// </summary>
        public abstract string Type { get; }

        #endregion // Properties

        #region Public methods
        /// <summary>
        /// Reset the selected column names for this type of searcher.
        /// </summary>
        public abstract void ResetColumns();

        /// <summary>
        /// Get the full detail for the specified value.
        /// </summary>
        /// <param name="value">The value for which full detail is obtained.</param>
        /// <returns>The full detail for the specified value.</returns>
        public object GetDetail(object value) =>
            CanGetDetail ? GetDetailAsync(value).GetAwaiter().GetResult() : value;

        /// <summary>
        /// Asynchronously get the full detail for the specified value.
        /// </summary>
        /// <param name="value">The value for which full detail is obtained.</param>
        /// <returns>The full detail for the specified value.</returns>
        public async Task<object> GetDetailAsync(object value)
        {
            StartOperation();

            try
            {
                if (value == null || !CanGetDetail)
                    return null;

                return await GetDetailInternal(value);
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>m.Body.ToString().Substring(0,1000)
        /// Search synchronously, paging as necessary.
        /// </summary>
        /// <returns>The result of the search.</returns>
        public SearchResult Search() =>
            SearchAsync()
                .GetAwaiter()
                .GetResult();

        /// <summary>
        /// Search asynchronously, paging as necessary.
        /// </summary>
        /// <returns>The result of the search.</returns>
        public abstract Task<SearchResult> SearchAsync();
        #endregion // Public methods

        #region Protected methods
        /// <summary>,
        /// Asynchronously get the full detail for the specified value.
        /// </summary>
        /// <param name="value">The value for which full detail is obtained.</param>
        /// <returns>The full detail for the specified value.</returns>
        /// <remarks>
        /// The GitHub API lacks an obvious method for directly accessing the
        /// <see cref="SearchCode"/> type, so we simply return the original value.
        /// </remarks>
#pragma warning disable CS1998
        protected virtual async Task<object> GetDetailInternal(object value) =>
            value;
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        /// <summary>
        /// End an operation.
        /// </summary>
        protected void EndOperation() =>
            isBusy = 0;

        /// <summary>
        /// Start an operation.
        /// </summary>
        protected void StartOperation()
        {
            // Prevent the start of a new search, while an existing search is already active.
            if (Interlocked.CompareExchange(ref isBusy, 1, 0) == 1)
                throw new InvalidOperationException();
        }
        #endregion // Protected methods
    }

    /// <summary>
    /// An abstraction to get strongly typed search results, using one of the GitHub API
    /// search methods.
    /// </summary>
    public abstract class Searcher<TRequest, TItem> : Searcher
        where TRequest : BaseSearchRequest
    {
        #region Constructors
        /// <summary>
        /// Create a searcher for the specified client, limited to the specified number of items,
        /// with the specified search criteria.
        /// </summary>
        /// <param name="client">The GitHub client.</param>
        /// <param name="maximumCount">The maximum number of items to return.</param>
        /// <param name="request">The request/search criteria.</param>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="client"/> or <paramref name="request"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// <paramref name="maximumCount"/> is less than or equal to zero.
        /// </exception>
        protected Searcher(GitHubClient client, int maximumCount, TRequest request)
            : base(client, maximumCount)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));

        }
        #endregion // Constructors

        #region Properties
        /// <summary>
        /// Gets the request/criteria for this searcher.
        /// </summary>
        public TRequest Request { get; }
        #endregion // Properties

        #region Public methods
        /// <summary>
        /// Search asynchronously, paging as necessary.
        /// </summary>
        /// <returns>The result of the search.</returns>
        public override async Task<SearchResult> SearchAsync()
        {
            StartOperation();

            try
            {
                var list = new List<TItem>();
                int remainingCount = MaximumCount;
                bool incompleteResults = false;
                TotalCount = Count = null;

                while (remainingCount > 0 && !(BackgroundWorker?.CancellationPending ?? false))
                {
                    SearchResultsPage result = await GetNextPageAsync();
                    if (!TotalCount.HasValue)
                        TotalCount = result.TotalCount;

                    incompleteResults = result.IncompleteResults;
                    IReadOnlyList<TItem> items = result.Items;

                    int count = Math.Min(remainingCount, items.Count);

                    list.AddRange(items.Take(count));
                    remainingCount -= count;
                    Count += count;

                    if (incompleteResults || items.Count < Request.PerPage)
                        break;

                    Request.Page++;
                }

                IsCancelled = BackgroundWorker?.CancellationPending ?? false;

                return new SearchResult<TItem>(TotalCount ?? 0, incompleteResults,
                    new ReadOnlyCollection<TItem>(list));
            }
            finally
            {
                EndOperation();
            }
        }
        #endregion // Public methods

        #region Protected methods
        /// <summary>
        /// Get the next page of search results asynchronously.
        /// </summary>
        /// <returns>The next page of search results.</returns>
        protected abstract Task<SearchResultsPage> GetNextPageAsync();
        #endregion // Protected methods

        #region Protected structures
        /// <summary>
        /// An broker for the common elements of a single page of search results.
        /// </summary>
        /// <remarks>
        /// It was possible to use the parent type <see cref="Octokit.Internal.SearchResult{T}"/>,
        /// but the "Internal" designation, in the namespace, made this author worry that this might
        /// be a no-no.
        /// </remarks>
        protected struct SearchResultsPage
        {
            #region Constructors
            /// <summary>
            /// Create a response for the specified total count, incomplete results indicator,
            /// and items.
            /// </summary>
            /// <param name="totalCount">The total number of matching items from the search.</param>
            /// <param name="incompleteResults">A value indicating if the search was incomplete.</param>
            /// <param name="items">The matching items from the search.</param>
            public SearchResultsPage(int totalCount, bool incompleteResults, IReadOnlyList<TItem> items)
            {
                IncompleteResults = incompleteResults;
                TotalCount = totalCount;
                Items = items;
                ExportPullRequestToXML(items);
               // UpdatePullRequest();
            }
            #endregion // Constructors
            
            /// <summary>
            /// Export PullRequest To XML
            /// </summary>
            /// <param name="items"></param>
            public async void ExportPullRequestToXML(IReadOnlyList<TItem> items)
            {
                #region Pull Requests with More than One Issues 
                var project = "joda-time";
                var path = "JodaOrg";
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\PullRequests" + project + ".xml");
                XmlNode rootNode = xmlDoc["PullRequests"];

                foreach (var item in items)
                {
                    Octokit.Issue m = item as Octokit.Issue;

                    /*Checked it is not robot,check issue is mentioned in pullrequest*/
                    if (m != null)
                    {
                        XmlNode PullRequestNode = xmlDoc.CreateElement("PullRequest");
                        XmlElement elemID = xmlDoc.CreateElement("IssueID");
                        elemID.InnerText = m.Number.ToString();
                        PullRequestNode.AppendChild(elemID);

                        XmlElement elemRepoID = xmlDoc.CreateElement("RepoID");
                        elemRepoID.InnerText = project;
                        PullRequestNode.AppendChild(elemRepoID);

                        XmlElement elemFixedByID = xmlDoc.CreateElement("FixedByID");
                        elemFixedByID.InnerText = m.User.Login;
                        PullRequestNode.AppendChild(elemFixedByID);

                        XmlElement elemTitle = xmlDoc.CreateElement("Title");
                        elemTitle.InnerText = RemoveSpecialCharacters(m.Title?.ToString());
                        PullRequestNode.AppendChild(elemTitle);

                        XmlElement elemDescription = xmlDoc.CreateElement("Description");
                        //elemDescription.InnerText = RemoveSpecialCharacters(m.Body?.ToString());
                        elemDescription.InnerText = m.Body?.ToString();
                        PullRequestNode.AppendChild(elemDescription);

                        XmlElement elemOpenedAt = xmlDoc.CreateElement("CreatedDate");
                        elemOpenedAt.InnerText = m.CreatedAt.ToString("dd/MM/yyyy");
                        PullRequestNode.AppendChild(elemOpenedAt);

                        XmlElement elemClosedAt = xmlDoc.CreateElement("ClosedDate");
                        elemClosedAt.InnerText = m.ClosedAt?.ToString("dd/MM/yyyy");
                        PullRequestNode.AppendChild(elemClosedAt);

                        XmlElement pullrequestLabels = xmlDoc.CreateElement("PullRequestLabels");
                        foreach (var _label in m.Labels)
                        {
                            XmlElement label = xmlDoc.CreateElement("PullRequestLabel");
                            label.InnerText = _label.Name;
                            pullrequestLabels.AppendChild(label);
                        }
                        PullRequestNode.AppendChild(pullrequestLabels);

                        var issues = HasIssueNo(m.Body?.ToString());
                        if (issues != null && issues.Count() > 0)
                        {
                            /*Get Issue List*/
                            XmlElement IssuesNodes = xmlDoc.CreateElement("Issues");
                            foreach (var issue in issues)
                            {
                                var _tempissue = MainForm.GetIssue(path, project, Convert.ToInt32(issue.Replace("#", "")));
                                if (!_tempissue.IsCompleted)
                                {
                                    var tempIssue = await _tempissue.ConfigureAwait(false);
                                    Octokit.Issue _issue = tempIssue as Octokit.Issue;
                                    /*Get Only Class File (*.cs)*/
                                    if (_issue != null)
                                    {
                                        XmlElement IssuesNode = xmlDoc.CreateElement("Issue");
                                        if (_issue.Labels != null && _issue.Labels.Count() > 0)
                                        {
                                            XmlElement issue_RepoID = xmlDoc.CreateElement("RepoID");
                                            issue_RepoID.InnerText = project;
                                            IssuesNode.AppendChild(issue_RepoID);

                                            XmlElement parent_IssueID = xmlDoc.CreateElement("PullRequestID");
                                            parent_IssueID.InnerText = m.Number.ToString();
                                            IssuesNode.AppendChild(parent_IssueID);

                                            XmlElement issueID = xmlDoc.CreateElement("IssueID");
                                            issueID.InnerText = _issue.Number.ToString();
                                            IssuesNode.AppendChild(issueID);

                                            XmlElement issueTitle = xmlDoc.CreateElement("Title");
                                            issueTitle.InnerText = _issue.Title?.ToString();
                                            IssuesNode.AppendChild(issueTitle);                                          

                                            if (!string.IsNullOrEmpty(_issue.Body))
                                            {
                                                XmlElement issueDescription = xmlDoc.CreateElement("Description");
                                                issueDescription.InnerText = _issue.Body;

                                                var codeblockcount = Regex.Matches(issueDescription.InnerText, "```").Count;
                                                if (codeblockcount > 1)
                                                {
                                                    /*Remove Code from Description*/
                                                    var noofcodeblock = codeblockcount / 2;
                                                    for (int i = 0; i <= noofcodeblock; i++)
                                                    {
                                                        var _valueD = issueDescription.InnerText;

                                                        int startIndex_D = _valueD.IndexOf("```");
                                                        int endIndex_D = _valueD.LastIndexOf("```");
                                                        int length_D = endIndex_D - startIndex_D + 1;
                                                        if (startIndex_D > -1 && endIndex_D > -1)
                                                        {
                                                            _valueD = _valueD.Remove(startIndex_D, length_D);
                                                            issueDescription.InnerText = _valueD;
                                                        }
                                                    }

                                                    XmlElement issueCode = xmlDoc.CreateElement("Code");
                                                    string tempdata = _issue.Body?.ToString();
                                                    /*Extract Code -- if csharp exist*/
                                                    for (int i = 0; i <= noofcodeblock; i++)
                                                    {
                                                        /*Extract Code from Description*/
                                                        var _valueDCode = tempdata;
                                                        int startIndex_DCode = _valueDCode.IndexOf("```");
                                                        int endIndex_DCode = _valueDCode.LastIndexOf("```");
                                                        int length_DCode = endIndex_DCode - startIndex_DCode + 1;
                                                        if (startIndex_DCode > -1 && endIndex_DCode > -1)
                                                        {
                                                            _valueDCode = _valueDCode.Substring(startIndex_DCode, length_DCode);
                                                            if (_valueDCode.Contains("csharp") || _valueDCode.Contains("c#") || _valueDCode.Contains("cs") || _valueDCode.Contains("java"))
                                                            {
                                                                issueCode.InnerText = issueCode.InnerText + _valueDCode;
                                                            }
                                                            tempdata = tempdata.Remove(startIndex_DCode, length_DCode);
                                                        }
                                                    }
                                                    if (!string.IsNullOrEmpty(issueCode.InnerText))
                                                    {
                                                        issueCode.InnerText = issueCode.InnerText.Replace("```", string.Empty);
                                                        issueCode.InnerText = issueCode.InnerText.Replace("&gt;", ">");
                                                        issueCode.InnerText = issueCode.InnerText.Replace("&lt;", "<");
                                                        IssuesNode.AppendChild(issueCode);
                                                    }

                                                }

                                                issueDescription.InnerText = Regex.Replace(issueDescription.InnerText, @"http[^\s]+", ""); /*Removed URL*/
                                                issueDescription.InnerText = Regex.Replace(issueDescription.InnerText, @"(?<!\r)\n", String.Empty); /*Removed line Break*/
                                                //issueDescription.InnerText = RemoveSpecialCharacters(issueDescription.InnerText
                                                issueDescription.InnerText = issueDescription.InnerText;
                                                IssuesNode.AppendChild(issueDescription);
                                            }

                                            XmlElement issueOpenedAt = xmlDoc.CreateElement("CreatedDate");
                                            issueOpenedAt.InnerText = _issue.CreatedAt.ToString("dd/MM/yyyy");
                                            IssuesNode.AppendChild(issueOpenedAt);

                                            XmlElement issueClosedAt = xmlDoc.CreateElement("ClosedDate");
                                            issueClosedAt.InnerText = _issue.ClosedAt?.ToString("dd/MM/yyyy");
                                            IssuesNode.AppendChild(issueClosedAt);

                                            XmlElement issueLabels = xmlDoc.CreateElement("Labels");
                                            foreach (var _label in _issue.Labels)
                                            {
                                                XmlElement label = xmlDoc.CreateElement("Label");
                                                label.InnerText = _label.Name;
                                                issueLabels.AppendChild(label);
                                            }
                                            IssuesNode.AppendChild(issueLabels);

                                            /*Add to Parent Issue*/
                                            IssuesNodes.AppendChild(IssuesNode);
                                        }
                                    }
                                }
                            }
                            PullRequestNode.AppendChild(IssuesNodes);
                            rootNode.AppendChild(PullRequestNode);

                            ///*Get File List*/
                            //var files = MainForm.GetPullRequestFiles(path, project, Convert.ToInt32(m.Number));
                            //if (!files.IsCompleted)
                            //{
                            //    var tempFiles = await files.ConfigureAwait(false);
                            //    IReadOnlyList<PullRequestFile> _Files = tempFiles as IReadOnlyList<PullRequestFile>;
                            //    /*Get Only Class File (*.cs)*/
                            //    if (_Files != null && _Files.Count() > 0 && _Files.Where(x => x.FileName.EndsWith(".cs")).Any())
                            //    {
                            //        _Files = _Files.Where(x => x.FileName.EndsWith(".cs")).ToList();
                            //        XmlElement FilesNode = xmlDoc.CreateElement("Files");
                            //        XmlElement parent_FileID = xmlDoc.CreateElement("PullRequestID");
                            //        parent_FileID.InnerText = m.Number.ToString();

                            //        XmlElement File_RepoID = xmlDoc.CreateElement("RepoID");
                            //        File_RepoID.InnerText = project;
                            //        FilesNode.AppendChild(File_RepoID);

                            //        FilesNode.AppendChild(parent_FileID);

                            //        foreach (var file in _Files)
                            //        {
                            //            if (file != null)
                            //            {
                            //                XmlElement File = xmlDoc.CreateElement("File");
                            //                File.InnerText = file.FileName;
                            //                FilesNode.AppendChild(File);
                            //            }
                            //        }
                            //        PullRequestNode.AppendChild(FilesNode);
                            //        rootNode.AppendChild(PullRequestNode);
                            //    }

                            //}


                        }
                    }
                }
                xmlDoc.Save("C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\PullRequests" + project + ".xml");
                #endregion Pull Requests with More than One Issues
            }

            /// <summary>
            /// Update Pull Requests
            /// </summary>
            public async void UpdatePullRequest()
            {
                string project = "roslyn";
                var path = @"C:\\PhD\\Workbrench\\GitHub_NeturalNetworks\\Datasets\\PullRequests" + project + ".xml";

                XmlDocument document = new XmlDocument();
                document.Load(path);

                XmlNode rootNode = document["PullRequests"];
                XmlNodeList xnPullRequests = rootNode.SelectNodes("PullRequest");
                /*Pull Requests*/
                foreach (XmlNode pxn in xnPullRequests)
                {
                    /*Issues*/
                    XmlNodeList xnIssues = pxn.SelectNodes("Issues");
                    if (xnIssues != null)
                    {
                        foreach (XmlNode sxns in xnIssues)
                        {
                            /*Issue*/
                            XmlNodeList xnIssue = sxns.SelectNodes("Issue");

                            foreach (XmlNode sxn in xnIssue)
                            {
                                /*Labels*/
                                XmlNodeList xnLabels = sxn.SelectNodes("Labels");
                               
                                foreach (XmlNode slbs in xnLabels)
                                {
                                    /*Label*/
                                    XmlNodeList xnLabel = slbs.SelectNodes("Label");

                                    foreach (XmlNode slb in xnLabel)
                                    {
                                        XmlNode llabel = document.CreateElement("Name");
                                        llabel.InnerText = slb.InnerText;
                                        slb.AppendChild(llabel);

                                        XmlNode lissueId = document.CreateElement("IssueID");
                                        lissueId.InnerText = sxn["IssueID"].InnerText;
                                        slb.AppendChild(lissueId);

                                        XmlElement lRepoID = document.CreateElement("RepoID");
                                        lRepoID.InnerText = pxn["RepoID"].InnerText;
                                        slb.AppendChild(lRepoID);
                                    }

                                }
                            }
                        }
                    }
                }
                document.Save(path);
            }

            #region Properties
                /// <summary>
                /// Gets the total number of matching items from the search.
                /// </summary>
            public int TotalCount { get; }

            /// <summary>
            /// Gets a value indicating if the search was incomplete.
            /// </summary>
            public bool IncompleteResults { get; }

            /// <summary>
            /// Gets the matching items from the search.
            /// </summary>
            public IReadOnlyList<TItem> Items { get; }
            #endregion // Properties
        }
        #endregion // Protected structures

        private static List<string> MoreThanOneIssuesFix(string content)
        {
            var pattern = @"#\d+";
            List<string> result = new List<string>();
            if (!string.IsNullOrEmpty(content))
            {
                Regex rgx = new Regex(pattern, RegexOptions.IgnoreCase);
                if (rgx.Matches(content).Count > 1)
                {
                    foreach (Match match in rgx.Matches(content))
                    {
                        result.Add(match.Value);
                    }
                }
            }
            return result;
        }

        private static List<string> HasIssueNo(string content)
        {
            var pattern = @"#\d+";
            List<string> result = new List<string>();
            if (!string.IsNullOrEmpty(content))
            {
                Regex rgx = new Regex(pattern, RegexOptions.IgnoreCase);
                if (rgx.Matches(content).Count > 0)
                {
                    foreach (Match match in rgx.Matches(content))
                    {
                        result.Add(match.Value);
                    }
                }
            }
            return result;
        }

        private static string RemoveSpecialCharacters(string str)
        {
            if (!String.IsNullOrEmpty(str))
            {
                StringBuilder sb = new StringBuilder();
                foreach (char c in str)
                {
                    if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '.' || c == '_')
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }
            return str;
        }
    }
}
