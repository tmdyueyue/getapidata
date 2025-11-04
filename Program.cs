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
        /// 写入日志
        /// </summary>
        public static void WriteLog(string strLog)
        {
            try
            {
                StreamWriter stream;
                string path = AppDomain.CurrentDomain.BaseDirectory;

                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                string logFile = Path.Combine(path, "log.txt");
                stream = new StreamWriter(logFile, true, Encoding.UTF8);
                stream.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + strLog);
                stream.Write("\r\n");
                stream.Flush();
                stream.Close();
                
                // 同时输出到控制台
                Console.WriteLine($"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")} - {strLog}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入日志失败: {ex.Message}");
            }
        }
    }
}
