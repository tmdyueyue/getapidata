using System;
using System.IO;
using System.Text;

namespace U_up
{
    /// <summary>
    /// 主程序类
    /// </summary>
    public class Program
    {
        public static readonly object logLock = new object(); // 日志写入锁（公开，供ApiDataFetcher使用）

        static void Main(string[] args)
        {
            Console.WriteLine("数据抓取服务启动中，请勿关闭!");
            Console.WriteLine("=====================================");
            
            try
            {
                // 创建数据库辅助类
                DatabaseHelper dbHelper = new DatabaseHelper(Config.MySqlConnectionString);
                
                // 创建API数据抓取类
                ApiDataFetcher fetcher = new ApiDataFetcher(dbHelper);
                
                // 遍历所有配置的接口
                WriteLog($"开始处理 {Config.ApiConfigs.Count} 个接口");
                
                foreach (var apiConfig in Config.ApiConfigs)
                {
                    try
                    {
                        WriteLog($"=====================================");
                        WriteLog($"开始处理接口: {apiConfig.ApiName}");
                        
                        // 获取分页数据
                        fetcher.FetchPaginatedData(apiConfig);
                        
                        WriteLog($"接口 {apiConfig.ApiName} 处理完成");
                        
                        // 接口之间添加延迟
                        System.Threading.Thread.Sleep(2000);
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"处理接口 {apiConfig.ApiName} 时发生错误: {ex.Message}");
                        WriteLog($"错误详情: {ex.StackTrace}");
                    }
                }
                
                WriteLog("=====================================");
                WriteLog("所有接口处理完成!");
                Console.WriteLine("所有接口处理完成!");
            }
            catch (Exception ex)
            {
                WriteLog($"程序执行失败: {ex.Message}");
                WriteLog($"错误详情: {ex.StackTrace}");
                Console.WriteLine($"程序执行失败: {ex.Message}");
            }
            
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }

        /// <summary>
        /// 写入日志（线程安全）
        /// </summary>
        public static void WriteLog(string strLog)
        {
            lock (logLock)
            {
                try
                {
                    string path = AppDomain.CurrentDomain.BaseDirectory;

                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }

                    string logFile = Path.Combine(path, "log.txt");
                    string logMessage = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - {strLog}";
                    
                    // 使用File.AppendAllText，它是原子操作，更安全
                    File.AppendAllText(logFile, logMessage + "\r\n", Encoding.UTF8);
                    
                    // 同时输出到控制台（控制台输出也需要加锁，避免混乱）
                    Console.WriteLine(logMessage);
                }
                catch (Exception ex)
                {
                    // 如果写入日志失败，至少输出到控制台
                    try
                    {
                        Console.WriteLine($"[日志写入失败] {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - {strLog}");
                        Console.WriteLine($"错误: {ex.Message}");
                    }
                    catch
                    {
                        // 如果连控制台输出都失败，忽略
                    }
                }
            }
        }
    }
}
