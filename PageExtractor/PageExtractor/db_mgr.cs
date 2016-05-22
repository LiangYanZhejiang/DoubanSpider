using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace PageExtractor
{
    internal sealed class cmd_opts
    {
        public const int __cache_default = 200;

        public static string _db_path = "PageExtractor.db3";
        //[Option("c", "dbcache", HelpText = "数据文件写入缓存(n).  [default: 500]")]
        public static int _db_cache = __cache_default;
    }
    internal sealed class db_mgr
    {
        // Fields (4) 
        private int _cache_cnt = cmd_opts.__cache_default;
        /// <summary>
        /// 抓取数据存储文件是否创建的标示.
        /// </summary>
        private bool _db_created;
        private string _db_path;
        private DateTime _latestRoundTime;
        private List<UnitInfo> _insertBook_cache = new List<UnitInfo>();
        private List<UnitInfo> _updateBook_cache = new List<UnitInfo>();
        private List<UrlInfo> _insertUrl_cache = new List<UrlInfo>();
        private List<UrlInfo> _updateUrl_cache = new List<UrlInfo>();
        private HashSet<string> _LoadedBookUrl = new HashSet<string>();
        private HashSet<string> _LoadedWebUrl = new HashSet<string>();
        private readonly object _bookLocker = new object();
        private readonly object _urlLocker = new object();
        // Constructors (2) 

        public db_mgr(string db_path, int cache_cnt)
            : this(db_path)
        {
            _cache_cnt = cache_cnt;
        }

        public db_mgr(string db_path)
        {
            _db_path = db_path;
            _db_created = File.Exists(_db_path);
        }

        // Methods (2) 

        // Public Methods (1) 
        public void writeAll()
        {
            lock (_bookLocker)
            {
                lock (_urlLocker)
                {
                    insertBookinfoToDb();
                    updateBookinfoToDb();
                    insertWebrlToDb();
                    updateWeburlToDb();
                }
            }

            
        }
        public void write_to_db(UnitInfo bookInfo)
        {
            lock (_bookLocker)
            {
                if (_LoadedBookUrl.Contains(bookInfo._WebUrl))
                    _updateBook_cache.Add(bookInfo);
                else
                    _insertBook_cache.Add(bookInfo);

                if (_insertBook_cache.Count >= _cache_cnt)
                    insertBookinfoToDb();

                if (_updateBook_cache.Count >= _cache_cnt)
                    updateBookinfoToDb();                
            }
        }

        public void write_to_db(UrlInfo urlInfo)
        {
            lock (_urlLocker)
            {
                if (_LoadedWebUrl.Contains(urlInfo._WebUrl))
                    _updateUrl_cache.Add(urlInfo);
                else
                    _insertUrl_cache.Add(urlInfo);

                if (_insertUrl_cache.Count >= _cache_cnt)
                    insertWebrlToDb();

                if (_updateUrl_cache.Count >= _cache_cnt)
                    updateWeburlToDb();
            }
        }

        private void insertBookinfoToDb()
        { 
            //写入数据库
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + _db_path))
            {
                conn.Open();
                using (SQLiteTransaction tran = conn.BeginTransaction())
                {
                    foreach (UnitInfo cache in _insertBook_cache)
                    {
                        if (_LoadedBookUrl.Contains(cache._WebUrl))
                        {
                            _updateBook_cache.Add(cache);
                            continue;
                        }

                        SQLiteCommand cmd = new SQLiteCommand(conn);
                        cmd.Transaction = tran;
                        cmd.CommandText = @"insert into BookInfo(WebUrl, Author, Publisher, PublishDate, 
                                                PageNum, Price, ISBN, AverageScore, RatingNum,
                                                FiveStar, FourStar, ThreeStar, TwoStar, OneStar, 
                                                Tags, ContentDescription, AuthorDescription) 
                                                values(@WebUrl, @Author, @Publisher, @PublishDate, 
                                                @PageNum, @Price, @ISBN, @AverageScore, @RatingNum,
                                                @FiveStar, @FourStar, @ThreeStar, @TwoStar, @OneStar, 
                                                @Tags, @ContentDescription, @AuthorDescription)";

                        cmd.Parameters.AddRange(new[] {
								new SQLiteParameter("@WebUrl", cache._WebUrl),
                                new SQLiteParameter("@Author", cache._Author),
                                new SQLiteParameter("@Publisher", cache._Publish),
                                new SQLiteParameter("@PublishDate", cache._PublishTime),
                                new SQLiteParameter("@PageNum", cache._PageNum),
                                new SQLiteParameter("@Price", cache._Price),
                                new SQLiteParameter("@ISBN", cache._ISBN),
                                new SQLiteParameter("@AverageScore", cache._AverageScore),
                                new SQLiteParameter("@RatingNum", cache._RatingNum),
                                new SQLiteParameter("@FiveStar", cache._star[0]),
                                new SQLiteParameter("@FourStar", cache._star[1]),
                                new SQLiteParameter("@ThreeStar", cache._star[2]),
                                new SQLiteParameter("@TwoStar", cache._star[3]),
                                new SQLiteParameter("@OneStar", cache._star[4]),
                                new SQLiteParameter("@Tags", cache._tags),
                                new SQLiteParameter("@ContentDescription", cache._Content),
                                new SQLiteParameter("@AuthorDescription", cache._AuthorDesc)
							});
                        cmd.ExecuteNonQuery();

                        _LoadedBookUrl.Add(cache._WebUrl);
                    }
                    tran.Commit();
                }
            }
            _insertBook_cache.Clear();
        }

        private void insertWebrlToDb()
        {
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + _db_path))
            {
                conn.Open();
                using (SQLiteTransaction tran = conn.BeginTransaction())
                {
                    foreach (UrlInfo cache in _insertUrl_cache)
                    {
                        if (_LoadedWebUrl.Contains(cache._WebUrl))
                        {
                            if( cache._updateTime != null)
                                _updateUrl_cache.Add(cache);
                            continue;
                        }

                        SQLiteCommand cmd = new SQLiteCommand(conn);
                        cmd.Transaction = tran;
                        cmd.CommandText = @"insert into UrlInfo(WebUrl, UrlType, HttpStatus, CreateTime) 
                                                values(@WebUrl, @UrlType, @HttpStatus, @CreateTime)";

                        cmd.Parameters.AddRange(new[] {
								new SQLiteParameter("@WebUrl", cache._WebUrl),
                                new SQLiteParameter("@UrlType", cache._UrlType),
                                new SQLiteParameter("@HttpStatus", cache._HttpStatus),
                                new SQLiteParameter("@CreateTime", cache._creatTime)
							});
                        cmd.ExecuteNonQuery();

                        _LoadedWebUrl.Add(cache._WebUrl);
                    }
                    tran.Commit();
                }
            }
            _insertUrl_cache.Clear();
        }


        private void updateBookinfoToDb()
        {
            //写入数据库
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + _db_path))
            {
                conn.Open();
                using (SQLiteTransaction tran = conn.BeginTransaction())
                {
                    foreach (UnitInfo cache in _updateBook_cache)
                    {
                        SQLiteCommand cmd = new SQLiteCommand(conn);
                        cmd.Transaction = tran;
                        cmd.CommandText = @"update BookInfo set Author = @Author, Publisher = @Publisher, 
                                                PublishDate= @PublishDate, PageNum = @PageNum, Price = @Price,
                                                ISBN= @ISBN, AverageScore = @AverageScore, RatingNum = @RatingNum,
                                                FiveStar = @FiveStar, FourStar = @FourStar, ThreeStar = @ThreeStar,
                                                TwoStar = @TwoStar, OneStar = @OneStar, Tags = @Tags, 
                                                ContentDescription = @ContentDescription, AuthorDescription = @AuthorDescription 
                                                where WebUrl = @WebUrl;";

                        cmd.Parameters.AddRange(new[] {
								new SQLiteParameter("@WebUrl", cache._WebUrl),
                                new SQLiteParameter("@Author", cache._Author),
                                new SQLiteParameter("@Publisher", cache._Publish),
                                new SQLiteParameter("@PublishDate", cache._PublishTime),
                                new SQLiteParameter("@PageNum", cache._PageNum),
                                new SQLiteParameter("@Price", cache._Price),
                                new SQLiteParameter("@ISBN", cache._ISBN),
                                new SQLiteParameter("@AverageScore", cache._AverageScore),
                                new SQLiteParameter("@RatingNum", cache._RatingNum),
                                new SQLiteParameter("@FiveStar", cache._star[0]),
                                new SQLiteParameter("@FourStar", cache._star[1]),
                                new SQLiteParameter("@ThreeStar", cache._star[2]),
                                new SQLiteParameter("@TwoStar", cache._star[3]),
                                new SQLiteParameter("@OneStar", cache._star[4]),
                                new SQLiteParameter("@Tags", cache._tags),
                                new SQLiteParameter("@ContentDescription", cache._Content),
                                new SQLiteParameter("@AuthorDescription", cache._AuthorDesc)
							});
                        cmd.ExecuteNonQuery();
                    }
                    tran.Commit();
                }
            }
            _updateBook_cache.Clear();
        }

        private void updateWeburlToDb()
        {
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + _db_path))
            {
                conn.Open();
                using (SQLiteTransaction tran = conn.BeginTransaction())
                {
                    foreach (UrlInfo cache in _updateUrl_cache)
                    {
                        if (_LoadedWebUrl.Contains(cache._WebUrl))
                        {
                            _updateUrl_cache.Add(cache);
                            continue;
                        }

                        SQLiteCommand cmd = new SQLiteCommand(conn);
                        cmd.Transaction = tran;
                        cmd.CommandText = @"update UrlInfo set HttpStatus = @HttpStatus, UpdateTime = @UpdateTime
                                            LatestReqTime = @LatestReqTime where WebUrl = @WebUrl)";

                        cmd.Parameters.AddRange(new[] {
								new SQLiteParameter("@WebUrl", cache._WebUrl),
                                new SQLiteParameter("@HttpStatus", cache._HttpStatus),
                                new SQLiteParameter("@UpdateTime", cache._updateTime),
                                new SQLiteParameter("@LatestReqTime", _latestRoundTime)
							});
                        cmd.ExecuteNonQuery();

                        _LoadedWebUrl.Add(cache._WebUrl);
                    }
                    tran.Commit();
                }
            }
            _updateUrl_cache.Clear();
        }

        /// <summary>
        /// 创建sqlite数据库, 和表.
        /// </summary>
        private void create_db()
        {
            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + _db_path))
            using (SQLiteCommand cmd = new SQLiteCommand(conn))
            {
                conn.Open();
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS [BookInfo] (
                                          [BookID] integer primary key AutoIncrement,
                                          [WebUrl] NVARCHAR(200) UNIQUE, 
                                          [Author] NVARCHAR(200),
                                          [Publisher] NVARCHAR(200), 
                                          [PublishDate] NVARCHAR(50),
                                          [PageNum] int,
                                          [Price] NVARCHAR(50),
                                          [ISBN] NVARCHAR(50),
                                          [AverageScore] float,
                                          [RatingNum] nchar(10),
                                          [FiveStar] float,
                                          [FourStar] float,
                                          [ThreeStar] float,
                                          [TwoStar] float,
                                          [OneStar] float,
                                          [ContentDescription] NVARCHAR(5000),
                                          [AuthorDescription] NVARCHAR(5000),
                                          [Tags] NVARCHAR(50));";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS [UrlInfo] (
                                          [ID] integer primary key AutoIncrement,
                                          [WebUrl] NVARCHAR(200) UNIQUE,
                                          [UrlType] NVARCHAR(50),
                                          [HttpStatus] NVARCHAR(50), 
                                          [CreateTime] datetime,
                                          [UpdateTime] datetime,
                                          [LatestReqTime] datetime);";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"INSERT INTO UrlInfo (WebUrl, UrlType, CreateTime) VALUES (@WebUrl, @UrlType, @CreateTime);";
                DateTime dt = DateTime.Now;
                string dt24 = dt.ToString("yyyy-MM-dd HH:mm:ss"); 
                cmd.Parameters.AddRange(new[] {
								new SQLiteParameter("@WebUrl", "https://book.douban.com/tag/?view=type&icn=index-sorttags-all"),
                                new SQLiteParameter("@UrlType", UrlType.TagsUrl.ToString()),
                                new SQLiteParameter("@CreateTime", dt24)
							});
                cmd.ExecuteNonQuery();            

                conn.Close();
            }
        }
        public void Init_loaddb(Dictionary<string, UrlType> urlsLoaded, Dictionary<string, UrlType> urlsUnload)
        {
            if (!_db_created)
            {                
               create_db();
                _db_created = true;
            }

            using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + _db_path))
            using (SQLiteCommand cmd = new SQLiteCommand(conn))
            {
                conn.Open();
                cmd.CommandText = @"SELECT WebUrl FROM BookInfo;";
                SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    _LoadedBookUrl.Add(reader.GetString(0));
                }
                reader.Close();

                cmd.CommandText = @"SELECT WebUrl FROM UrlInfo;";
                reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    _LoadedWebUrl.Add(reader.GetString(0));
                }
                reader.Close();

                //判断要不要进行新的一轮的请求
                int WebUrlTotalCount = 0;
                cmd.CommandText = @"SELECT count(*) FROM UrlInfo;";
                reader = cmd.ExecuteReader();
                if (reader.Read())
                    WebUrlTotalCount = reader.GetInt32(0);

                reader.Close();

                int latestRoundCount = 0;
                cmd.CommandText = @"SELECT LatestReqTime, count(*) FROM UrlInfo Where LatestReqTime is not null GROUP by LatestReqTime order by LatestReqTime desc;";
                reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    _latestRoundTime = reader.GetDateTime(0);
                    latestRoundCount = reader.GetInt32(1);
                }
                else
                {
                    _latestRoundTime = DateTime.Now;
                }
                reader.Close();

                if (latestRoundCount == WebUrlTotalCount)
                {
                    _latestRoundTime = DateTime.Now;
                    cmd.CommandText = @"SELECT WebUrl FROM UrlInfo Where UrlType = 0;";
                    reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        urlsUnload.Add(reader.GetString(0), 0);
                    }
                    reader.Close();
                }
                else
                {
                    cmd.CommandText = @"SELECT WebUrl, UrlType FROM UrlInfo Where LatestReqTime <> @LatestReqTime or HttpStatus <> @HttpStatus or latestreqtime is null or httpstatus is null;";
                    cmd.Parameters.AddRange(new[] {
                                new SQLiteParameter("@LatestReqTime", _latestRoundTime),
                                new SQLiteParameter("@HttpStatus", System.Net.HttpStatusCode.OK.ToString())});
                    reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                         urlsUnload.Add(reader.GetString(0), (UrlType)Enum.Parse(typeof(UrlType), reader.GetString(1)));
                    }
                    reader.Close();

                    cmd.CommandText = @"SELECT WebUrl, UrlType FROM UrlInfo Where LatestReqTime = @LatestReqTime and HttpStatus = @HttpStatus;";
                    cmd.Parameters.AddRange(new[] {
                                new SQLiteParameter("@LatestReqTime", _latestRoundTime),
                                new SQLiteParameter("@HttpStatus", System.Net.HttpStatusCode.OK.ToString()) });
                    reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        urlsLoaded.Add(reader.GetString(0), (UrlType)Enum.Parse(typeof(UrlType), reader.GetString(1)));
                    }
                    reader.Close();
                }

                conn.Close();  
            }
        }
    }
}
