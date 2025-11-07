using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace U_up
{
    /// <summary>
    /// API数据抓取类
    /// </summary>
    public class ApiDataFetcher
    {
        private CookieContainer cookieContainer = new CookieContainer();
        private DatabaseHelper dbHelper;

        public ApiDataFetcher(DatabaseHelper dbHelper)
        {
            this.dbHelper = dbHelper;
        }

        /// <summary>
        /// 登录（如果需要）
        /// </summary>
        public bool Login(string loginUrl, string loginData)
        {
            try
            {
                string response = HttpPost(loginUrl, loginData);
                JObject result = JObject.Parse(response);
                string msg = result["msg"]?.ToString() ?? "";
                
                if (msg == "SUCCESS" || response.Contains("\"success\"") || response.Contains("\"code\":200"))
                {
                    WriteLog("登录成功");
                    return true;
                }
                else
                {
                    WriteLog($"登录失败: {response}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                WriteLog($"登录异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取分页数据（并行版本，支持projectId循环）
        /// </summary>
        public void FetchPaginatedData(ApiConfig config)
        {
            WriteLog($"开始获取接口数据: {config.ApiName}");

            // 如果需要登录，先登录
            if (config.RequireLogin && !string.IsNullOrEmpty(config.LoginUrl))
            {
                if (!Login(config.LoginUrl, config.LoginData))
                {
                    WriteLog($"接口 {config.ApiName} 登录失败，跳过");
                    return;
                }
            }

            // 如果需要循环projectId
            if (config.NeedProjectIdLoop)
            {
                // 获取projectId列表
                List<int> projectIdList = new List<int>();
                if (config.ProjectIdList != null && config.ProjectIdList.Count > 0)
                {
                    projectIdList = config.ProjectIdList;
                }
                else
                {
                    // 从ProjectIdStart到ProjectIdEnd循环
                    for (int i = config.ProjectIdStart; i <= config.ProjectIdEnd; i++)
                    {
                        projectIdList.Add(i);
                    }
                }

                WriteLog($"需要循环 {projectIdList.Count} 个projectId: {config.ProjectIdStart} 到 {config.ProjectIdEnd}");

                // 对每个projectId循环处理
                foreach (int projectId in projectIdList)
                {
                    WriteLog($"=====================================");
                    WriteLog($"开始处理 projectId: {projectId}");
                    FetchPaginatedDataForProjectId(config, projectId);
                    WriteLog($"projectId {projectId} 处理完成");
                    WriteLog($"=====================================");
                    
                    // projectId之间添加延迟
                    System.Threading.Thread.Sleep(1000);
                }
            }
            else
            {
                // 不需要循环projectId，直接处理
                FetchPaginatedDataForProjectId(config, 0);
            }

            WriteLog($"接口 {config.ApiName} 数据获取完成");
        }

        /// <summary>
        /// 获取指定projectId的分页数据
        /// </summary>
        private void FetchPaginatedDataForProjectId(ApiConfig config, int projectId)
        {
            // 先获取第一页数据，确定总页数（带重试机制）
            int totalPages = 0;
            int totalRecords = 0;
            
            // 添加重试机制，最多重试6次
            int maxRetries = 6;
            int retryCount = 0;
            bool success = false;
            
            while (retryCount < maxRetries && !success)
            {
                try
                {
                    var firstPageResult = FetchSinglePage(config, 1, projectId);
                    if (firstPageResult == null)
                    {
                        throw new Exception("返回数据为空");
                    }

                    totalPages = firstPageResult.TotalPages;
                    totalRecords = firstPageResult.TotalRecords;
                    success = true;

                    WriteLog($"projectId {projectId} 总共有 {totalPages} 页，共 {totalRecords} 条记录");
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (ex.Message.Contains("timeout") || ex.Message.Contains("time out") || ex.Message.Contains("timed out"))
                    {
                        if (retryCount < maxRetries)
                        {
                            WriteLog($"projectId {projectId} 获取第一页数据超时，正在重试 ({retryCount}/{maxRetries})...");
                            // 等待一段时间后重试（递增延迟：3秒、6秒、9秒...）
                            System.Threading.Thread.Sleep(3000 * retryCount);
                        }
                        else
                        {
                            WriteLog($"projectId {projectId} 获取第一页数据失败: {ex.Message} (已重试 {maxRetries} 次)");
                            return;
                        }
                    }
                    else
                    {
                        // 非超时错误，直接返回
                        WriteLog($"projectId {projectId} 获取第一页数据失败: {ex.Message}");
                        return;
                    }
                }
            }
            
            if (!success)
            {
                WriteLog($"projectId {projectId} 获取第一页数据失败，无法确定总页数");
                return;
            }

            if (totalPages <= 0)
            {
                WriteLog($"projectId {projectId} 总页数为0，无需获取数据");
                return;
            }

            // 并行处理页数（默认为5个并发任务，可以根据需要调整）11111111111111111111111111111111111111111111111111111111111
            int maxConcurrency = 50; // 最大并发数
            WriteLog($"projectId {projectId} 开始并行获取数据，最大并发数: {maxConcurrency}");

            // 创建所有页面的任务列表（跳过第1页，因为已经在获取总页数时获取过了）
            List<int> pageList = new List<int>();
            for (int i = 2; i <= totalPages; i++)
            {
                pageList.Add(i);
            }
            
            // 如果只有1页，则不需要并行获取（第1页已经在上面获取过了）
            if (pageList.Count == 0)
            {
                WriteLog($"projectId {projectId} 只有1页数据，已在获取总页数时完成，无需并行获取");
                return;
            }

            // 使用SemaphoreSlim限制并发数
            var semaphore = new System.Threading.SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>();

            foreach (int page in pageList)
            {
                int currentPage = page; // 捕获变量
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // 为每个任务创建独立的数据库连接和HTTP客户端
                        // 添加重试机制，最多重试3次
                        int maxRetries = 6;
                        int retryCount = 0;
                        bool success = false;
                        
                        while (retryCount < maxRetries && !success)
                        {
                            try
                            {
                                FetchSinglePageWithIndependentConnection(config, currentPage, projectId);
                                success = true;
                            }
                            catch (Exception ex)
                            {
                                retryCount++;
                                if (ex.Message.Contains("timeout") || ex.Message.Contains("time out") || ex.Message.Contains("timed out"))
                                {
                                    if (retryCount < maxRetries)
                                    {
                                        WriteLog($"projectId {projectId} 第 {currentPage} 页请求超时，正在重试 ({retryCount}/{maxRetries})...");
                                        // 等待一段时间后重试
                                        await Task.Delay(2000 * retryCount); // 递增延迟：2秒、4秒、6秒
                                    }
                                    else
                                    {
                                        WriteLog($"projectId {projectId} 并行获取第 {currentPage} 页数据失败: {ex.Message} (已重试 {maxRetries} 次)");
                                        // 记录失败的页面到日志
                                        WriteSkippedPageLog(config, currentPage, $"超时失败: {ex.Message}", projectId);
                                    }
                                }
                                else
                                {
                                    // 非超时错误，直接抛出
                                    WriteLog($"projectId {projectId} 并行获取第 {currentPage} 页数据失败: {ex.Message}");
                                    WriteSkippedPageLog(config, currentPage, $"请求失败: {ex.Message}", projectId);
                                    throw;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"projectId {projectId} 并行获取第 {currentPage} 页数据失败: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            // 等待所有任务完成
            Task.WaitAll(tasks.ToArray());

            WriteLog($"projectId {projectId} 数据获取完成，共 {totalRecords} 条记录，已获取 {totalPages} 页");
        }

        /// <summary>
        /// 获取单页数据
        /// </summary>
        private PageResult FetchSinglePage(ApiConfig config, int pageNumber, int projectId = 0)
        {
            try
            {
                // 构建URL
                string url = config.UrlTemplate;
                string response = "";
                
                // 根据HTTP方法选择GET或POST
                if (config.HttpMethod?.ToUpper() == "POST")
                {
                    // POST请求：构建请求体
                    string postBody = "";
                    if (!string.IsNullOrEmpty(config.PostBodyTemplate))
                    {
                        // 如果PostBodyTemplate包含{2}，说明需要projectId参数
                        if (config.PostBodyTemplate.Contains("{2}"))
                        {
                            postBody = string.Format(config.PostBodyTemplate, pageNumber, config.PageSize, projectId);
                        }
                        else
                        {
                            postBody = string.Format(config.PostBodyTemplate, pageNumber, config.PageSize);
                        }
                    }
                    else
                    {
                        // 如果没有指定模板，使用默认格式
                        if (config.NeedProjectIdLoop && projectId > 0)
                        {
                            postBody = $"{{\"{config.PageParamName}\":{pageNumber},\"{config.PageSizeParamName}\":{config.PageSize},\"total\":0,\"entity\":{{\"projectId\":{projectId}}}}}";
                        }
                        else
                        {
                            postBody = $"{{\"{config.PageParamName}\":{pageNumber},\"{config.PageSizeParamName}\":{config.PageSize}}}";
                        }
                    }
                    
                    if (projectId > 0)
                    {
                        WriteLog($"正在POST projectId {projectId} 第 {pageNumber} 页数据: {url}");
                    }
                    else
                    {
                        WriteLog($"正在POST第 {pageNumber} 页数据: {url}");
                    }
                    
                    response = HttpPost(url, postBody, config.Authorization);
                }
                else
                {
                    // GET请求：构建URL参数
                    if (url.Contains("?"))
                    {
                        url = $"{url}&{config.PageParamName}={pageNumber}&{config.PageSizeParamName}={config.PageSize}";
                    }
                    else
                    {
                        url = $"{url}?{config.PageParamName}={pageNumber}&{config.PageSizeParamName}={config.PageSize}";
                    }
                    
                    WriteLog($"正在GET第 {pageNumber} 页数据: {url}");
                    
                    response = HttpGet(url, config.Authorization);
                }
                
                if (string.IsNullOrEmpty(response))
                {
                    WriteLog($"第 {pageNumber} 页返回数据为空");
                    return null;
                }

                // 解析JSON
                JObject jsonResponse = JObject.Parse(response);

                // 获取总记录数和总页数
                int totalPages = 0;
                int totalRecords = 0;
                
                JToken totalToken = jsonResponse;
                string[] totalPathParts = config.TotalFieldName.Split('.');
                foreach (string part in totalPathParts)
                {
                    totalToken = totalToken[part];
                    if (totalToken == null) break;
                }

                if (totalToken != null)
                {
                    totalRecords = totalToken.Value<int>();
                    totalPages = (int)Math.Ceiling((double)totalRecords / config.PageSize);
                }

                // 尝试从返回数据中获取总页数
                try
                {
                    JToken pagesToken = jsonResponse["data"]?["pages"];
                    if (pagesToken != null)
                    {
                        totalPages = pagesToken.Value<int>();
                    }
                }
                catch { }

                // 提取数据数组
                JToken dataToken = jsonResponse;
                string[] dataPathParts = config.DataPath.Split('.');
                foreach (string part in dataPathParts)
                {
                    dataToken = dataToken[part];
                    if (dataToken == null) break;
                }

                if (dataToken == null || !(dataToken is JArray dataArray) || dataArray.Count == 0)
                {
                    WriteLog($"第 {pageNumber} 页没有数据");
                    return new PageResult { TotalPages = totalPages, TotalRecords = totalRecords };
                }

                // 保存数据到数据库
                var (inserted, skipped) = dbHelper.BatchInsertData(config.TableName, dataArray);
                if (skipped > 0)
                {
                    WriteLog($"第 {pageNumber} 页数据保存完成，共 {dataArray.Count} 条记录，实际插入 {inserted} 条，跳过 {skipped} 条（重复数据）");
                }
                else
                {
                    WriteLog($"第 {pageNumber} 页数据保存成功，共 {dataArray.Count} 条记录，实际插入 {inserted} 条");
                }

                return new PageResult { TotalPages = totalPages, TotalRecords = totalRecords };
            }
            catch (Exception ex)
            {
                WriteLog($"获取第 {pageNumber} 页数据失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 获取单页数据（使用独立的数据库连接，线程安全）
        /// </summary>
        private void FetchSinglePageWithIndependentConnection(ApiConfig config, int pageNumber, int projectId = 0)
        {
            try
            {
                // 为每个任务创建独立的数据库连接
                DatabaseHelper independentDbHelper = new DatabaseHelper(Config.MySqlConnectionString);
                
                // 构建URL
                string url = config.UrlTemplate;
                string response = "";
                
                // 根据HTTP方法选择GET或POST
                if (config.HttpMethod?.ToUpper() == "POST")
                {
                    // POST请求：构建请求体
                    string postBody = "";
                    if (!string.IsNullOrEmpty(config.PostBodyTemplate))
                    {
                        // 如果PostBodyTemplate包含{2}，说明需要projectId参数
                        if (config.PostBodyTemplate.Contains("{2}"))
                        {
                            postBody = string.Format(config.PostBodyTemplate, pageNumber, config.PageSize, projectId);
                        }
                        else
                        {
                            postBody = string.Format(config.PostBodyTemplate, pageNumber, config.PageSize);
                        }
                    }
                    else
                    {
                        // 如果没有指定模板，使用默认格式
                        if (config.NeedProjectIdLoop && projectId > 0)
                        {
                            postBody = $"{{\"{config.PageParamName}\":{pageNumber},\"{config.PageSizeParamName}\":{config.PageSize},\"total\":0,\"entity\":{{\"projectId\":{projectId}}}}}";
                        }
                        else
                        {
                            postBody = $"{{\"{config.PageParamName}\":{pageNumber},\"{config.PageSizeParamName}\":{config.PageSize}}}";
                        }
                    }
                    
                    if (projectId > 0)
                    {
                        WriteLog($"正在POST projectId {projectId} 第 {pageNumber} 页数据: {url}");
                    }
                    else
                    {
                        WriteLog($"正在POST第 {pageNumber} 页数据: {url}");
                    }
                    
                    // 使用独立的CookieContainer
                    response = HttpPostWithNewCookie(url, postBody, config.Authorization);
                }
                else
                {
                    // GET请求：构建URL参数
                    if (url.Contains("?"))
                    {
                        url = $"{url}&{config.PageParamName}={pageNumber}&{config.PageSizeParamName}={config.PageSize}";
                    }
                    else
                    {
                        url = $"{url}?{config.PageParamName}={pageNumber}&{config.PageSizeParamName}={config.PageSize}";
                    }
                    
                    WriteLog($"正在GET第 {pageNumber} 页数据: {url}");
                    
                    // 使用独立的CookieContainer
                    response = HttpGetWithNewCookie(url, config.Authorization);
                }
                
                if (string.IsNullOrEmpty(response))
                {
                    WriteLog($"第 {pageNumber} 页返回数据为空");
                    // 记录没有返回数据的页面信息到日志
                    WriteSkippedPageLog(config, pageNumber, "返回数据为空");
                    return;
                }

                // 解析JSON
                JObject jsonResponse = JObject.Parse(response);

                // 提取数据数组
                JToken dataToken = jsonResponse;
                string[] dataPathParts = config.DataPath.Split('.');
                foreach (string part in dataPathParts)
                {
                    dataToken = dataToken[part];
                    if (dataToken == null) break;
                }

                if (dataToken == null || !(dataToken is JArray dataArray) || dataArray.Count == 0)
                {
                    WriteLog($"第 {pageNumber} 页没有数据");
                    // 记录没有数据的页面信息到日志
                
                    return;
                }

                // 保存数据到数据库（使用独立的数据库连接）
                var (inserted, skipped) = independentDbHelper.BatchInsertData(config.TableName, dataArray);
                if (skipped > 0)
                {
                    WriteLog($"第 {pageNumber} 页数据保存完成，共 {dataArray.Count} 条记录，实际插入 {inserted} 条，跳过 {skipped} 条（重复数据）");
                }
                else
                {
                    WriteLog($"第 {pageNumber} 页数据保存成功，共 {dataArray.Count} 条记录，实际插入 {inserted} 条");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"获取第 {pageNumber} 页数据失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// HTTP GET 请求（使用新的CookieContainer）
        /// </summary>
        private string HttpGetWithNewCookie(string url, string authorization = null)
        {
            CookieContainer newCookieContainer = new CookieContainer();
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "*/*";
            request.Timeout = 600000; // 增加超时时间到120秒（2分钟）
            request.CookieContainer = newCookieContainer;
            request.AllowAutoRedirect = false;
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
            
            // 添加Authorization头
            if (!string.IsNullOrEmpty(authorization))
            {
                request.Headers.Add("Authorization", authorization);
            }

            WebResponse response = null;
            string responseStr = null;
            try
            {
                response = request.GetResponse();
                if (response != null)
                {
                    StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
                    responseStr = reader.ReadToEnd();
                    reader.Close();
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse httpResponse)
                {
                    string errorDetails = $"HTTP {((int)httpResponse.StatusCode)} {httpResponse.StatusDescription}";
                    WriteLog($"HTTP GET 请求失败: {url}");
                    WriteLog($"错误详情: {errorDetails}");
                    
                    // 尝试读取错误响应内容
                    try
                    {
                        using (Stream errorStream = httpResponse.GetResponseStream())
                        {
                            if (errorStream != null)
                            {
                                StreamReader errorReader = new StreamReader(errorStream, Encoding.UTF8);
                                string errorContent = errorReader.ReadToEnd();
                                WriteLog($"错误响应内容: {errorContent}");
                            }
                        }
                    }
                    catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                WriteLog($"HTTP GET 请求失败: {url}");
                WriteLog($"错误: {ex.Message}");
                throw;
            }
            finally
            {
                request = null;
                response?.Close();
            }
            return responseStr;
        }

        /// <summary>
        /// HTTP POST 请求（使用新的CookieContainer）
        /// </summary>
        private string HttpPostWithNewCookie(string url, string dataJSON, string authorization = null)
        {
            CookieContainer newCookieContainer = new CookieContainer();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json;charset=UTF-8";
            request.CookieContainer = newCookieContainer;
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
            request.Timeout = 600000; // 增加超时时间到120秒（2分钟）
            
            // 添加Authorization头
            if (!string.IsNullOrEmpty(authorization))
            {
                request.Headers.Add("Authorization", authorization);
            }

            string paraUrlCoded = dataJSON;
            byte[] payload = Encoding.UTF8.GetBytes(paraUrlCoded);
            request.ContentLength = payload.Length;

            try
            {
                Stream writer = request.GetRequestStream();
                writer.Write(payload, 0, payload.Length);
                writer.Close();

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                Stream s = response.GetResponseStream();
                string StrDate = "";
                string strValue = "";
                StreamReader Reader = new StreamReader(s, Encoding.UTF8);
                while ((StrDate = Reader.ReadLine()) != null)
                {
                    strValue += StrDate + "\r\n";
                }
                return strValue;
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse httpResponse)
                {
                    string errorDetails = $"HTTP {((int)httpResponse.StatusCode)} {httpResponse.StatusDescription}";
                    WriteLog($"HTTP POST 请求失败: {url}");
                    WriteLog($"请求体: {dataJSON}");
                    WriteLog($"错误详情: {errorDetails}");
                    
                    // 尝试读取错误响应内容
                    try
                    {
                        using (Stream errorStream = httpResponse.GetResponseStream())
                        {
                            if (errorStream != null)
                            {
                                StreamReader errorReader = new StreamReader(errorStream, Encoding.UTF8);
                                string errorContent = errorReader.ReadToEnd();
                                WriteLog($"错误响应内容: {errorContent}");
                            }
                        }
                    }
                    catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                WriteLog($"HTTP POST 请求失败: {url}");
                WriteLog($"请求体: {dataJSON}");
                WriteLog($"错误: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 页面结果类
        /// </summary>
        private class PageResult
        {
            public int TotalPages { get; set; }
            public int TotalRecords { get; set; }
        }

        /// <summary>
        /// 写入未执行页面日志
        /// </summary>
        private static void WriteSkippedPageLog(ApiConfig config, int pageNumber, string reason, int projectId = 0)
        {
            lock (Program.logLock)
            {
                try
                {
                    string path = AppDomain.CurrentDomain.BaseDirectory;
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }

                    string skippedLogFile = Path.Combine(path, "skipped_page_log.txt");
                    string logMessage = "";
                    if (projectId > 0)
                    {
                        logMessage = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] 接口: {config.ApiName}, 表: {config.TableName}, projectId: {projectId}, 页码: {pageNumber}, 原因: {reason}";
                    }
                    else
                    {
                        logMessage = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] 接口: {config.ApiName}, 表: {config.TableName}, 页码: {pageNumber}, 原因: {reason}";
                    }
                    File.AppendAllText(skippedLogFile, logMessage + "\r\n", Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    WriteLog($"写入未执行页面日志失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// HTTP GET 请求
        /// </summary>
        private string HttpGet(string url, string authorization = null)
        {
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "*/*";
            request.Timeout = 600000; // 增加超时时间到600秒（10分钟）
            request.CookieContainer = cookieContainer;
            request.AllowAutoRedirect = false;
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
            
            // 添加Authorization头
            if (!string.IsNullOrEmpty(authorization))
            {
                request.Headers.Add("Authorization", authorization);
            }

            WebResponse response = null;
            string responseStr = null;
            try
            {
                response = request.GetResponse();
                if (response != null)
                {
                    StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
                    responseStr = reader.ReadToEnd();
                    reader.Close();
                }
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse httpResponse)
                {
                    string errorDetails = $"HTTP {((int)httpResponse.StatusCode)} {httpResponse.StatusDescription}";
                    WriteLog($"HTTP GET 请求失败: {url}");
                    WriteLog($"错误详情: {errorDetails}");
                    
                    // 尝试读取错误响应内容
                    try
                    {
                        using (Stream errorStream = httpResponse.GetResponseStream())
                        {
                            if (errorStream != null)
                            {
                                StreamReader errorReader = new StreamReader(errorStream, Encoding.UTF8);
                                string errorContent = errorReader.ReadToEnd();
                                WriteLog($"错误响应内容: {errorContent}");
                            }
                        }
                    }
                    catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                WriteLog($"HTTP GET 请求失败: {url}");
                WriteLog($"错误: {ex.Message}");
                throw;
            }
            finally
            {
                request = null;
                response?.Close();
            }
            return responseStr;
        }

        /// <summary>
        /// HTTP POST 请求
        /// </summary>
        private string HttpPost(string url, string dataJSON, string authorization = null)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json;charset=UTF-8";
            request.CookieContainer = cookieContainer;
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
            request.Timeout = 600000; // 增加超时时间到600秒（10分钟）
            
            // 添加Authorization头
            if (!string.IsNullOrEmpty(authorization))
            {
                request.Headers.Add("Authorization", authorization);
            }

            string paraUrlCoded = dataJSON;
            byte[] payload = Encoding.UTF8.GetBytes(paraUrlCoded);
            request.ContentLength = payload.Length;

            try
            {
                Stream writer = request.GetRequestStream();
                writer.Write(payload, 0, payload.Length);
                writer.Close();

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                foreach (Cookie ck in response.Cookies)
                {
                    cookieContainer.Add(ck);
                }

                Stream s = response.GetResponseStream();
                string StrDate = "";
                string strValue = "";
                StreamReader Reader = new StreamReader(s, Encoding.UTF8);
                while ((StrDate = Reader.ReadLine()) != null)
                {
                    strValue += StrDate + "\r\n";
                }
                return strValue;
            }
            catch (WebException ex)
            {
                if (ex.Response is HttpWebResponse httpResponse)
                {
                    string errorDetails = $"HTTP {((int)httpResponse.StatusCode)} {httpResponse.StatusDescription}";
                    WriteLog($"HTTP POST 请求失败: {url}");
                    WriteLog($"请求体: {dataJSON}");
                    WriteLog($"错误详情: {errorDetails}");
                    
                    // 尝试读取错误响应内容
                    try
                    {
                        using (Stream errorStream = httpResponse.GetResponseStream())
                        {
                            if (errorStream != null)
                            {
                                StreamReader errorReader = new StreamReader(errorStream, Encoding.UTF8);
                                string errorContent = errorReader.ReadToEnd();
                                WriteLog($"错误响应内容: {errorContent}");
                            }
                        }
                    }
                    catch { }
                }
                throw;
            }
            catch (Exception ex)
            {
                WriteLog($"HTTP POST 请求失败: {url}");
                WriteLog($"请求体: {dataJSON}");
                WriteLog($"错误: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 写入日志
        /// </summary>
        private static void WriteLog(string message)
        {
            Program.WriteLog(message);
        }
    }
}

