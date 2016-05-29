using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using System.Xml;
using Sgml;
using NLog;

namespace PageExtractor
{
    enum UrlType
    {
        TagsUrl = 0,
        BooksUrl = 1,
        OneBookUrl = 2,
        UrlTypeMax
    }
    internal sealed class UnitInfo
    {
        public const int StarMax = 5;

        public UInt64 _ID = 0;
        public string _WebUrl;
        public UInt64 _BookID = 0;
        public string _BookName;
        public string _Author;
        public string _PrimitiveName;
        public string _Translator;
        public string _Publish;
        public string _PublishTime;
        public string _PageNum;
        public string _Price;
        public string _ISBN;
        public decimal _AverageScore;
        public int _RatingNum;
        public decimal[] _star = new decimal[StarMax];
        public string _Content;
        public string _AuthorDesc;
        public string _tags;
    }

    internal sealed class UrlInfo
    {
        public string _WebUrl;
        public DateTime _creatTime;
        public DateTime _updateTime;
        public UrlType _UrlType;
        public string _HttpStatus;

        public UrlInfo(string webUrl, UrlType urlType)
        {
            _WebUrl = webUrl;
            _UrlType = urlType;
            _creatTime = DateTime.Now;
        }

        public UrlInfo(string webUrl, string httpStatus)
        {
            _WebUrl = webUrl;
            _updateTime = DateTime.Now;
            _HttpStatus = httpStatus;
        }
    }
    class Spider
    {
        NLog.Logger _log = NLog.LogManager.GetCurrentClassLogger();
        #region private type
        private class RequestState
        {
            private const int BUFFER_SIZE = 131072;
            private byte[] _data = new byte[BUFFER_SIZE];
            private StringBuilder _sb = new StringBuilder();

            public HttpWebRequest Req { get; private set; }
            public string Url { get; private set; }
            public UrlType WebUrlType { get; private set; }
            public int Index { get; private set; }
            public Stream ResStream { get; set; }
            public StringBuilder Html
            {
                get
                {
                    return _sb;
                }
            }

            public byte[] Data
            {
                get
                {
                    return _data;
                }
            }

            public int BufferSize
            {
                get
                {
                    return BUFFER_SIZE;
                }
            }

            public RequestState(HttpWebRequest req, string url, UrlType urltype, int index)
            {
                Req = req;
                Url = url;
                WebUrlType = urltype;
                Index = index;
            }
        }

        private class WorkingUnitCollection
        {
            private int _count;
            private bool[] _busy;

            public WorkingUnitCollection(int count)
            {
                _count = count;
                _busy = new bool[count];

                for (int i = 0; i < count; i++)
                {
                    _busy[i] = true;
                }
            }

            public void StartWorking(int index)
            {
                if (!_busy[index])
                {
                    _busy[index] = true;
                }
            }

            public void FinishWorking(int index)
            {
                if (_busy[index])
                {
                    _busy[index] = false;
                }
            }

            public bool IsFinished()
            {
                bool notEnd = false;
                foreach (var b in _busy)
                {
                    notEnd |= b;
                }
                return !notEnd;
            }

            public void WaitAllFinished()
            {
                while (true)
                {
                    if (IsFinished())
                    {
                        break;
                    }
                    Thread.Sleep(1000);
                }
            }

            public void AbortAllWork()
            {
                for (int i = 0; i < _count; i++)
                {
                    _busy[i] = false;
                }
            }
        }

        #endregion

        #region private fields
        private static Encoding GB18030 = Encoding.GetEncoding("GB18030");   // GB18030兼容GBK和GB2312
        private static Encoding UTF8 = Encoding.UTF8;
        private string _userAgent = "Mozilla/4.0 (compatible; MSIE 8.0; Windows NT 6.1; Trident/4.0)";
        private string _accept = "text/html";
        private string _method = "GET";
        private Encoding _encoding = UTF8;
        private Encodings _enc = Encodings.GB;
        private int _maxTime = 2 * 60 * 1000;

        private string _rootUrl = null;
        private string _baseUrl = null;
        private Dictionary<string, UrlType> _urlsLoaded = new Dictionary<string, UrlType>();
        private Dictionary<string, UrlType> _urlsUnloadTags = new Dictionary<string, UrlType>();
        /*bool url, need load first*/
        private Dictionary<string, UrlType> _urlsUnloadBooks = new Dictionary<string, UrlType>();
        private static db_mgr _dbm;

        private bool _stop = true;
        private Timer _checkTimer = null;
        private readonly object _locker = new object();
        private bool[] _reqsBusy = null;
        private int _reqCount = 4;
        private WorkingUnitCollection _workingSignals;
        #endregion

        #region constructors
        /// <summary>
        /// 创建一个Spider实例
        /// </summary>
        public Spider()
        {
        }
        #endregion

        #region properties
        /// <summary>
        /// 下载根Url
        /// </summary>
        public string RootUrl
        {
            get
            {
                return _rootUrl;
            }
            set
            {
                if (!value.Contains("http://") && !value.Contains("https://"))
                {
                    _rootUrl = "http://" + value;
                }
                else
                {
                    _rootUrl = value;
                }
                _baseUrl = _rootUrl.Replace("www.", "");
                _baseUrl = _baseUrl.Replace("http://", "");
                _baseUrl = _baseUrl.Replace("https://", "");
                _baseUrl = _baseUrl.TrimEnd('/');
            }
        }

        /// <summary>
        /// 网页编码类型
        /// </summary>
        public Encodings PageEncoding
        {
            get
            {
                return _enc;
            }
            set
            {
                _enc = value;
                switch (value)
                {
                    case Encodings.GB:
                        _encoding = GB18030;
                        break;
                    case Encodings.UTF8:
                        _encoding = UTF8;
                        break;
                }
            }
        }

        /// <summary>
        /// 下载最大连接数
        /// </summary>
        public int MaxConnection
        {
            get
            {
                return _reqCount;
            }
            set
            {
                _reqCount = value;
            }
        }
        #endregion

        #region public type
        public delegate void ContentsSavedHandler(string path, string url);

        public delegate void DownloadFinishHandler();

        public enum Encodings
        {
            UTF8,
            GB
        }
        #endregion

        #region events
        /// <summary>
        /// 正文内容被保存到本地后触发
        /// </summary>
        public event ContentsSavedHandler ContentsSaved = null;

        /// <summary>
        /// 全部链接下载分析完毕后触发
        /// </summary>
        public event DownloadFinishHandler DownloadFinish = null;
        #endregion

        #region public methods
        /// <summary>
        /// 开始下载
        /// </summary>
        public void Download()
        {
            if (string.IsNullOrEmpty(RootUrl))
            {
                return;
            }
            Init();
            StartDownload();
        }

        /// <summary>
        /// 终止下载
        /// </summary>
        public void Abort()
        {
            _stop = true;
            if (_workingSignals != null)
            {
                _workingSignals.AbortAllWork();
            }

            if (_dbm != null)
                _dbm.writeAll();
        }
        #endregion

        #region private methods
        private void StartDownload()
        {
            _log.Debug("StartDownload");
            _checkTimer = new Timer(new TimerCallback(CheckFinish), null, 0, 300);
            DispatchWork();
        }

        private void CheckFinish(object param)
        {
            if (_workingSignals.IsFinished())
            {
                _checkTimer.Dispose();
                _checkTimer = null;
                if (DownloadFinish != null)
                {
                    DownloadFinish();
                }
            }
        }

        private void DispatchWork()
        {
            for (int i = 0; i < _reqCount; i++)
            {
                if (!_reqsBusy[i])
                {
                    RequestResource(i);
                    Thread.Sleep(100);
                }
            }
        }

        private void Init()
        {
            _dbm = new db_mgr(cmd_opts._db_path, cmd_opts._db_cache);
            _urlsLoaded.Clear();
            _urlsUnloadTags.Clear();
            _urlsUnloadBooks.Clear();
            _dbm.Init_loaddb(_urlsLoaded, _urlsUnloadTags, _urlsUnloadBooks);
            _log.Debug("Init: _urlsLoaded.Count = {0}, tagunload.Count = {1}, book unload.Count = {2}.",
                       _urlsLoaded.Count, _urlsUnloadTags.Count, _urlsUnloadBooks.Count);
            _reqsBusy = new bool[_reqCount];
            _workingSignals = new WorkingUnitCollection(_reqCount);
            _stop = false;
        }

        private Tuple<string, UrlType> GetUrlAndType(int index)
        {
            if (_stop)
            {
                return null;
            }

            UrlType urltype;
            string url = "";
            lock (_locker)
            {
                if (_reqsBusy[index])
                    return null;

                if (_urlsUnloadBooks.Count <= 0 && _urlsUnloadTags.Count <= 0)
                {
                    _workingSignals.FinishWorking(index);
                    return null;
                }
                _reqsBusy[index] = true;
                _workingSignals.StartWorking(index);
                if (_urlsUnloadBooks.Count > 0)
                {
                    urltype = _urlsUnloadBooks.First().Value;
                    url = _urlsUnloadBooks.First().Key;
                    _urlsUnloadBooks.Remove(url);
                }
                else
                {
                    urltype = _urlsUnloadTags.First().Value;
                    url = _urlsUnloadTags.First().Key;
                    _urlsUnloadTags.Remove(url);
                }
                _urlsLoaded.Add(url, urltype);

            }
            return new Tuple<string, UrlType>(url, urltype);
        }

        private void RequestResource(int index)
        {
            var urlAndType = GetUrlAndType(index);
            if (urlAndType == null)
                return;

            string url = urlAndType.Item1;
            UrlType urltype = urlAndType.Item2;
            try
            {
                _log.Info("Request {0} Time:{1}.", url, DateTime.Now.ToString());

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = _method; //请求方法
                req.Accept = _accept; //接受的内容
                req.CookieContainer = GetCookie();
                req.UserAgent = _userAgent; //用户代理
                RequestState rs = new RequestState(req, url, urltype, index);
                var result = req.BeginGetResponse(new AsyncCallback(ReceivedResource), rs);
                ThreadPool.RegisterWaitForSingleObject(result.AsyncWaitHandle,
                        TimeoutCallback, rs, _maxTime, true);
            }
            catch (WebException we)
            {
                _log.Error("RequestResource: url={0}，HttpStatus={1}, Exception:{2}.", url, we.Status, we.Message);
                _log.Error(we.StackTrace);

                UrlInfo urlInfo = new UrlInfo(url, we.Status.ToString());
                _dbm.write_to_db(urlInfo);

                _reqsBusy[index] = false;
            }

            if (!_reqsBusy[index])
                RequestResource(index);
        }

        private CookieContainer GetCookie()
        {
            CookieContainer _cookie = new CookieContainer();
            _cookie.Add(new Cookie("bid", Utility.GetPseudoBIDString(), "/", ".douban.com"));
            return _cookie;
        }

        private void ReceivedResource(IAsyncResult ar)
        {
            RequestState rs = (RequestState)ar.AsyncState;
            HttpWebRequest req = rs.Req;
            string url = rs.Url;
            try
            {
                HttpWebResponse res = (HttpWebResponse)req.EndGetResponse(ar);
                if (_stop)
                {
                    res.Close();
                    req.Abort();
                    return;
                }
                if (res != null && res.StatusCode == HttpStatusCode.OK)
                {
                    Stream resStream = res.GetResponseStream();
                    rs.ResStream = resStream;
                    var result = resStream.BeginRead(rs.Data, 0, rs.BufferSize,
                        new AsyncCallback(ReceivedData), rs);
                }
                else
                {
                    res.Close();
                    rs.Req.Abort();
                    _reqsBusy[rs.Index] = false;
                }
            }
            catch (WebException we)
            {
                _log.Error("ReceivedResource: url = {0}, HttpStatus = {1}, Exception:{2}.", url, we.Status, we.Message);
                _log.Error(we.StackTrace);
                UrlInfo urlInfo = new UrlInfo(url, we.Status.ToString());
                _dbm.write_to_db(urlInfo);

                if (ContentsSaved != null)
                {
                    ContentsSaved(we.Status.ToString(), url);
                }

                _reqsBusy[rs.Index] = false;
            }
            catch (Exception e)
            {
                _log.Error("ReceivedResource: url = {0}, Exception:{1}.", url, e.Message);
                _log.Error(e.StackTrace);

                UrlInfo urlInfo = new UrlInfo(url, e.Message);
                _dbm.write_to_db(urlInfo);

                if (ContentsSaved != null)
                {
                    ContentsSaved(e.Message, url);
                }

                _reqsBusy[rs.Index] = false;
            }

            if (!_reqsBusy[rs.Index])
                RequestResource(rs.Index);
        }

        private void ReceivedData(IAsyncResult ar)
        {
            RequestState rs = (RequestState)ar.AsyncState;
            HttpWebRequest req = rs.Req;
            Stream resStream = rs.ResStream;
            string url = rs.Url;
            UrlType urltype = rs.WebUrlType;
            string html = null;
            int index = rs.Index;
            int read = 0;
            string HttpStatus;

            try
            {
                read = resStream.EndRead(ar);
                if (_stop)
                {
                    rs.ResStream.Close();
                    req.Abort();
                    return;
                }
                if (read > 0)
                {
                    MemoryStream ms = new MemoryStream(rs.Data, 0, read);
                    StreamReader reader = new StreamReader(ms, _encoding);
                    string str = reader.ReadToEnd();
                    rs.Html.Append(str);
                    var result = resStream.BeginRead(rs.Data, 0, rs.BufferSize,
                        new AsyncCallback(ReceivedData), rs);
                    return;
                }
                html = rs.Html.ToString();
                SgmlReader sgmlRreader = new SgmlReader();
                sgmlRreader.DocType = "HTML";
                sgmlRreader.InputStream = new StringReader(html);
                StringWriter sw = new StringWriter();
                XmlTextWriter writer = new XmlTextWriter(sw);
                writer.Formatting = Formatting.Indented;
                while (sgmlRreader.Read())
                {
                    if (sgmlRreader.NodeType != XmlNodeType.Whitespace)
                    {
                        writer.WriteNode(sgmlRreader, true);
                    }
                }

                SaveContents(sw.ToString(), url, urltype);
                HttpStatus = WebExceptionStatus.Success.ToString();
            }
            catch (WebException we)
            {
                _log.Error("ReceivedData: url = {0}, HttpStatus = {1}, Exception:{2}.", url, we.Status, we.Message);
                _log.Error(we.StackTrace);

                HttpStatus = we.Status.ToString();
            }
            catch (Exception e)
            {
                _log.Error("ReceivedData: url = {0}, Exception:{1}.", url, e.Message);
                _log.Error(e.StackTrace);

                HttpStatus = e.Message;
            }

            UrlInfo urlInfo = new UrlInfo(url, HttpStatus);
            _dbm.write_to_db(urlInfo);

            if (ContentsSaved != null)
            {
                ContentsSaved(HttpStatus, url);
            }

            _reqsBusy[index] = false;
            RequestResource(index);
        }

        string RemoveTab(string html, string tab)
        {
            string Left = string.Format("<{0}>", tab);
            string Right = string.Format("</{0}>", tab);
            while (true)
            {
                int iPos1 = html.IndexOf(Left);
                int iPos2 = html.IndexOf(Right);
                if (iPos1 == -1 || iPos2 == -1)
                    break;

                if (iPos1 > iPos2)
                    html = html.Replace(Right, "");
                else
                    html = html.Remove(iPos1, iPos2 - iPos1 + 9);
            }

            return html;
        }

        string[] GetHtmlTab(string html, string tab)
        {
            string Left = string.Format("<{0}>", tab);
            string Right = string.Format("</{0}>", tab);
            int count = 0;

            int iPos = 0;
            while (true)
            {
                int iPos1 = html.IndexOf(Left, iPos);
                int iPos2 = html.IndexOf(Right, iPos);
                if (iPos1 == -1 || iPos2 == -1)
                    break;

                if (iPos1 > iPos2)
                    iPos = iPos1;
                else
                {
                    count++;
                    iPos = iPos2;
                }
            }

            string[] tabs = new string[count];

            iPos = 0;
            int index = 0;
            while (true)
            {
                int iPos1 = html.IndexOf(Left, iPos);
                int iPos2 = html.IndexOf(Right, iPos);
                if (iPos1 == -1 || iPos2 == -1)
                    break;

                if (iPos1 > iPos2)
                    iPos = iPos1;
                else
                {
                    string tabContext = html.Substring(iPos1, iPos2 - iPos1 + tab.Length + 3);
                    tabContext.Replace("\n", "");
                    tabs[index] = tabContext;
                    index++;
                    iPos = iPos2;
                }
            }

            return tabs;
        }

        private void SaveContents(string xml, string url, UrlType urltype)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return;
            }

            paserDate(xml, url, urltype);
        }

        private void paserDate(string xml, string url, UrlType urlType)
        {
            XmlDocument document = new XmlDocument();
            document.LoadXml(xml);
            XmlElement root = document.DocumentElement;

            switch (urlType)
            {
                case UrlType.TagsUrl:
                    {
                        XmlNodeList nodeList = root.SelectNodes("//a[@class='tag']");

                        if (nodeList.Count != 0)
                        {
                            foreach (XmlNode node in nodeList)
                            {
                                string strHref = node.Attributes["href"].Value;
                                if (strHref != null)
                                {
                                    string link = strHref.Contains(_baseUrl) ? strHref : RootUrl + strHref;
                                    AddUrls(link, UrlType.BooksUrl);
                                }
                            }
                        }
                        break;
                    }

                case UrlType.BooksUrl:
                    {
                        XmlNodeList nodeList = root.SelectNodes("//a[@title]");
                        if (nodeList.Count != 0)
                        {
                            foreach (XmlNode node in nodeList)
                            {
                                string strHref = node.Attributes["href"].Value;
                                if (strHref != null)
                                {
                                    string link = strHref.Contains(_baseUrl) ? strHref : RootUrl + strHref;
                                    AddUrls(link, UrlType.OneBookUrl);
                                }
                            }
                        }

                        nodeList = root.SelectNodes("//div[@class='paginator']/a");

                        if (nodeList.Count != 0)
                        {
                            foreach (XmlNode node in nodeList)
                            {
                                string strHref = node.Attributes["href"].Value;
                                if (strHref != null)
                                {
                                    string link = strHref.Contains(_baseUrl) ? strHref : RootUrl + strHref;
                                    AddUrls(link, UrlType.BooksUrl);
                                }
                            }
                        }

                        break;
                    }
                case UrlType.OneBookUrl:
                    {
                        ParseBookHtml(xml, url);
                        break;
                    }
                default:
                    {
                        return;
                    }
            }
        }

        void ParseBookHtml(string xml, string url)
        {
            XmlDocument document = new XmlDocument();
            document.LoadXml(xml);
            XmlElement root = document.DocumentElement;

            UnitInfo bookInfo = new UnitInfo();

            bookInfo._WebUrl = url;

            //书名
            XmlNodeList nodeList = root.SelectNodes("//span[@property='v:itemreviewed']");
            if (nodeList.Count < 1)
                return;

            bookInfo._BookName = nodeList[0].InnerText;

            nodeList = root.SelectNodes("//div[@id='info']");
            if (nodeList.Count < 1)
                return;
            XmlNode node1 = nodeList[0];
            XmlNodeList childnodeList = node1.ChildNodes;
            bool isPublish = false;
            bool isYuanZuoMing = false;
            bool isPublishTime = false;
            bool isPageNum = false;
            bool isPrice = false;
            bool isISBN = false;
            foreach (XmlNode childnode in childnodeList)
            {
                if (childnode.InnerText.Contains("作者:"))
                {
                    if (childnode.HasChildNodes)
                    {
                        XmlNodeList childAuthorList = childnode.ChildNodes;
                        foreach (XmlNode childAuthornode in childAuthorList)
                        {
                            if (childAuthornode.Name.Equals("a"))
                            {
                                if (bookInfo._Author == null)
                                    bookInfo._Author = childAuthornode.InnerText.Trim();
                                else
                                    bookInfo._Author = string.Format(@"{0}\{1}", bookInfo._Author, childAuthornode.InnerText.Trim());
                            }
                        }
                    }
                    else
                        bookInfo._Author = childnode.InnerText.Replace("作者:", "").Replace("\n", "").Trim();
                }

                if (childnode.InnerText.Contains("译者:"))
                {
                    bookInfo._Translator = childnode.InnerText.Replace("译者:", "").Replace("\n", "").Trim();
                }

                if (isPublish)
                {
                    bookInfo._Publish = childnode.InnerText;
                    isPublish = false;
                }

                if (childnode.InnerText.Contains("出版社:"))
                {
                    isPublish = true;
                }

                if (isYuanZuoMing)
                {
                    bookInfo._PrimitiveName = childnode.InnerText;
                    isYuanZuoMing = false;
                }

                if (childnode.InnerText.Contains("原作名:"))
                {
                    isYuanZuoMing = true;
                }

                if (isPublishTime)
                {
                    bookInfo._PublishTime = childnode.InnerText;
                    isPublishTime = false;
                }

                if (childnode.InnerText.Contains("出版年:"))
                {
                    isPublishTime = true;
                }

                if (isPageNum)
                {
                    bookInfo._PageNum = childnode.InnerText;
                    isPageNum = false;
                }

                if (childnode.InnerText.Contains("页数:"))
                {
                    isPageNum = true;
                }

                if (isPrice)
                {
                    bookInfo._Price = childnode.InnerText;
                    isPrice = false;
                }

                if (childnode.InnerText.Contains("定价:"))
                {
                    isPrice = true;
                }

                if (isISBN)
                {
                    bookInfo._ISBN = childnode.InnerText;
                    isISBN = false;
                }

                if (childnode.InnerText.Contains("ISBN:"))
                {
                    isISBN = true;
                }
            }

            nodeList = root.SelectNodes("//strong[@property='v:average']");
            if (nodeList.Count < 1)
                return;

            if (!decimal.TryParse(nodeList[0].InnerText, out bookInfo._AverageScore))
                return;

            nodeList = root.SelectNodes("//span[@property='v:votes']");
            if (nodeList.Count < 1)
                return;
            bookInfo._RatingNum = Convert.ToInt32(nodeList[0].InnerText);

            nodeList = root.SelectNodes("//span[@class='rating_per']");

            int starNum = 0;
            foreach (XmlNode StarNode in nodeList)
            {
                if (starNum >= UnitInfo.StarMax)
                    break;

                string star = StarNode.InnerText.TrimEnd('%');

                decimal dStar;
                if (decimal.TryParse(star, out dStar))
                    bookInfo._star[starNum] = dStar / 100;
                starNum++;
            }

            bool isContent = false;
            bool isAuthorInfo = false;
            nodeList = root.SelectNodes("//div[@class='related_info']");
            if (nodeList.Count > 0)
            {
                nodeList = nodeList[0].HasChildNodes ? nodeList[0].ChildNodes : null;

                foreach (XmlNode spanNode in nodeList)
                {
                    XmlNode node = spanNode.HasChildNodes ? spanNode.FirstChild : null;
                    if (node == null)
                        continue;
                    if (isContent)
                    {
                        if (node.Name == "div")
                        {
                            bookInfo._Content = node.LastChild.InnerText;
                        }
                        else
                        {
                            if (node.NextSibling != null && node.NextSibling.Name == "span")
                                node = node.NextSibling;

                            bookInfo._Content = node.FirstChild.LastChild.InnerText;
                        }
                        isContent = false;
                        continue;
                    }

                    if (isAuthorInfo)
                    {
                        if (node.NextSibling != null && node.NextSibling.Name == "span")
                            node = node.NextSibling;

                        bookInfo._AuthorDesc = node.LastChild.InnerText;
                        isAuthorInfo = false;
                        continue;
                    }

                    if (node.InnerText.Equals("内容简介"))
                    {
                        isContent = true;
                    }

                    if (node.InnerText.Equals("作者简介"))
                        isAuthorInfo = true;

                }
            }

            nodeList = root.SelectNodes("//a[@class='  tag']");
            string tag = "";
            foreach (XmlNode tagNode in nodeList)
            {
                if (tag != "")
                    tag = string.Format("{0},{1}", tag, tagNode.InnerText);
                else
                    tag = tagNode.InnerText;

                /*string strHref = tagNode.Attributes["href"].Value;
                if (strHref != null)
                {
                    string link = strHref.Contains(_baseUrl) ? strHref : RootUrl + strHref;
                    AddUrls(link, UrlType.BooksUrl);
                }*/
            }

            bookInfo._tags = tag;
            _dbm.write_to_db(bookInfo);
        }

        private void TimeoutCallback(object state, bool timedOut)
        {
            if (timedOut)
            {
                RequestState rs = state as RequestState;
                if (rs != null)
                {
                    rs.Req.Abort();

                    _log.Error("TimeoutCallback: url={0}，HttpStatus={1}.", rs.Url, "Timeout");

                    UrlInfo urlInfo = new UrlInfo(rs.Url, "TimeoutCallback:TimeOut");
                    _dbm.write_to_db(urlInfo);

                    _reqsBusy[rs.Index] = false;
                    RequestResource(rs.Index);
                }

            }
        }

        private string[] GetLinks(string html)
        {
            const string pattern = @"http://([\w-]+\.)+[\w-]+(/[\w- ./?%&=]*)?";
            Regex r = new Regex(pattern, RegexOptions.IgnoreCase);
            MatchCollection m = r.Matches(html);
            string[] links = new string[m.Count];

            for (int i = 0; i < m.Count; i++)
            {
                links[i] = m[i].ToString();
            }
            return links;
        }

        private bool UrlExists(string url)
        {
            bool result = _urlsUnloadTags.ContainsKey(url);
            result |= _urlsUnloadBooks.ContainsKey(url);
            result |= _urlsLoaded.ContainsKey(url);
            return result;
        }

        private bool UrlAvailable(string url)
        {
            if (url.Contains(".jpg") || url.Contains(".gif")
                || url.Contains(".png") || url.Contains(".css")
                || url.Contains(".js"))
            {
                return false;
            }

            if (UrlExists(url))
            {
                return false;
            }

            return true;
        }

        private void AddUrls(string url, UrlType urlType)
        {
            if (urlType >= UrlType.UrlTypeMax)
            {
                return;
            }

            string cleanUrl = url.Trim();
            int end = cleanUrl.IndexOf(' ');
            if (end > 0)
            {
                cleanUrl = cleanUrl.Substring(0, end);
            }
            cleanUrl = cleanUrl.TrimEnd('/');
            if (UrlAvailable(cleanUrl))
            {
                if (cleanUrl.Contains("book.douban.com/tag") || cleanUrl.Contains("book.douban.com/subject"))
                {
                    if (urlType == UrlType.OneBookUrl)
                        _urlsUnloadBooks.Add(cleanUrl, urlType);
                    else
                        _urlsUnloadTags.Add(cleanUrl, urlType);
                    UrlInfo urlInfo = new UrlInfo(cleanUrl, urlType);
                    _dbm.write_to_db(urlInfo);
                }
                else
                {
                    _log.Debug("Try add url failed:{0}.", cleanUrl);
                    //do nothing
                }
            }
        }
        #endregion
    }
}
