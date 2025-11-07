using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using MySql.Data.MySqlClient;
using Newtonsoft.Json.Linq;

namespace U_up
{
    /// <summary>
    /// 数据库操作辅助类
    /// </summary>
    public class DatabaseHelper
    {
        private string connectionString;
        private static readonly object duplicateLogLock = new object(); // 重复数据日志写入锁
        private static readonly System.Threading.SemaphoreSlim dbConnectionSemaphore = new System.Threading.SemaphoreSlim(10, 10); // 限制数据库连接并发数为10

        public DatabaseHelper(string connectionString)
        {
            this.connectionString = connectionString;
        }

        /// <summary>
        /// 创建表（如果不存在）
        /// 根据JSON数据自动创建表结构
        /// </summary>
        public void CreateTableIfNotExists(string tableName, JObject sampleData)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    // 检查表是否存在
                    string checkTableSql = $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{tableName}'";
                    MySqlCommand checkCmd = new MySqlCommand(checkTableSql, conn);
                    int tableExists = Convert.ToInt32(checkCmd.ExecuteScalar());

                    if (tableExists == 0)
                    {
                        // 创建表
                        List<string> columns = new List<string>();
                        columns.Add("id INT AUTO_INCREMENT PRIMARY KEY");
                        columns.Add("created_at DATETIME DEFAULT CURRENT_TIMESTAMP");
                        columns.Add("updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

                        // 根据JSON数据添加列
                        foreach (var property in sampleData.Properties())
                        {
                            string columnName = property.Name;
                            string columnType = GetColumnType(property.Value);
                            columns.Add($"`{columnName}` {columnType}");
                        }

                        string createTableSql = $"CREATE TABLE `{tableName}` ({string.Join(", ", columns)}) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
                        MySqlCommand createCmd = new MySqlCommand(createTableSql, conn);
                        createCmd.ExecuteNonQuery();
                        WriteLog($"表 {tableName} 创建成功");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"创建表 {tableName} 失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 根据JSON值类型确定MySQL列类型
        /// </summary>
        private string GetColumnType(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                    return "BIGINT";
                case JTokenType.Float:
                    return "DECIMAL(18,2)";
                case JTokenType.Boolean:
                    return "BOOLEAN";
                case JTokenType.Date:
                    return "DATETIME";
                case JTokenType.String:
                    // 如果是很长的字符串，使用TEXT
                    string strValue = token.ToString();
                    if (strValue.Length > 255)
                        return "TEXT";
                    return "VARCHAR(500)";
                default:
                    return "TEXT";
            }
        }

        /// <summary>
        /// 插入数据到数据库
        /// </summary>
        public void InsertData(string tableName, JObject data)
        {
            try
            {
                using (MySqlConnection conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    List<string> columns = new List<string>();
                    List<string> values = new List<string>();
                    List<MySqlParameter> parameters = new List<MySqlParameter>();

                    foreach (var property in data.Properties())
                    {
                        string columnName = property.Name;
                        columns.Add($"`{columnName}`");
                        
                        string paramName = $"@param{parameters.Count}";
                        values.Add(paramName);
                        
                        MySqlParameter param = new MySqlParameter(paramName, GetMySqlDbType(property.Value.Type));
                        param.Value = GetValue(property.Value);
                        parameters.Add(param);
                    }

                    string insertSql = $"INSERT INTO `{tableName}` ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
                    MySqlCommand cmd = new MySqlCommand(insertSql, conn);
                    cmd.Parameters.AddRange(parameters.ToArray());
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
           
                WriteLog($"数据: {data.ToString()}");
                throw;
            }
        }

        /// <summary>
        /// 批量插入数据
        /// 返回插入统计信息：成功插入数量，跳过数量
        /// </summary>
        public (int inserted, int skipped) BatchInsertData(string tableName, JArray dataArray)
        {
            if (dataArray == null || dataArray.Count == 0)
                return (0, 0);

            int insertedCount = 0;
            int skippedCount = 0;

            // 使用信号量限制并发连接数
            dbConnectionSemaphore.Wait();
            try
            {
                // 添加重试机制
                int maxRetries = 3;
                int retryCount = 0;
                bool success = false;
                
                while (retryCount < maxRetries && !success)
                {
                    try
                    {
                        using (MySqlConnection conn = new MySqlConnection(connectionString))
                        {
                            // 连接超时时间在连接字符串中设置（默认15秒）
                            conn.Open();

                            // 检查表是否存在，如果不存在则创建表
                            // 需要扫描所有数据来确定每个字段的最大长度
                            if (dataArray.Count > 0)
                            {
                                CreateTableIfNotExistsWithAllData(conn, tableName, dataArray);
                            }

                            // 批量插入
                            foreach (JToken item in dataArray)
                            {
                                if (item is JObject data)
                                {
                                    bool result = InsertDataInternal(conn, tableName, data);
                                    if (result)
                                    {
                                        insertedCount++;
                                    }
                                    else
                                    {
                                        skippedCount++;
                                    }
                                }
                            }
                        }
                        
                        success = true; // 成功执行
                    }
                    catch (MySql.Data.MySqlClient.MySqlException mysqlEx)
                    {
                        retryCount++;
                        // 如果是连接错误，重试
                        if ((mysqlEx.Number == 1042 || mysqlEx.Number == 0) && retryCount < maxRetries) // 1042 = Unable to connect to any of the specified MySQL hosts
                        {
                            WriteLog($"数据库连接失败，正在重试 ({retryCount}/{maxRetries}): {mysqlEx.Message}");
                            System.Threading.Thread.Sleep(1000 * retryCount); // 递增延迟：1秒、2秒、3秒
                        }
                        else
                        {
                            // 其他MySQL错误或重试次数用完，抛出异常
                            WriteLog($"批量插入数据到 {tableName} 失败: {mysqlEx.Message}");
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        // 如果是连接错误，重试
                        if ((ex.Message.Contains("Unable to connect") || ex.Message.Contains("timeout") || ex.Message.Contains("time out")) && retryCount < maxRetries)
                        {
                            WriteLog($"数据库连接失败，正在重试 ({retryCount}/{maxRetries}): {ex.Message}");
                            System.Threading.Thread.Sleep(1000 * retryCount); // 递增延迟：1秒、2秒、3秒
                        }
                        else
                        {
                            // 其他错误或重试次数用完，抛出异常
                            WriteLog($"批量插入数据到 {tableName} 失败: {ex.Message}");
                            throw;
                        }
                    }
                }
                
                if (!success)
                {
                    throw new Exception("批量插入数据失败：重试次数用完");
                }
            }
            finally
            {
                dbConnectionSemaphore.Release();
            }

            return (insertedCount, skippedCount);
        }

        /// <summary>
        /// 根据所有数据创建表（扫描所有数据来确定列类型）
        /// </summary>
        private void CreateTableIfNotExistsWithAllData(MySqlConnection conn, string tableName, JArray dataArray)
        {
            try
            {
                // 检查表是否存在
                string checkTableSql = $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{tableName}'";
                MySqlCommand checkCmd = new MySqlCommand(checkTableSql, conn);
                int tableExists = Convert.ToInt32(checkCmd.ExecuteScalar());

                if (tableExists == 0)
                {
                    // 创建表
                    List<string> columns = new List<string>();
                    columns.Add("id INT AUTO_INCREMENT PRIMARY KEY");
                    columns.Add("created_at DATETIME DEFAULT CURRENT_TIMESTAMP");
                    columns.Add("updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

                    // 固定的列名，避免重复添加
                    HashSet<string> reservedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "id", "created_at", "updated_at"
                    };

                    // 收集所有字段及其最大长度
                    Dictionary<string, int> fieldMaxLengths = new Dictionary<string, int>();
                    Dictionary<string, JTokenType> fieldTypes = new Dictionary<string, JTokenType>();

                    // 扫描所有数据，找出每个字段的最大长度和类型
                    foreach (JToken item in dataArray)
                    {
                        if (item is JObject data)
                        {
                            foreach (var property in data.Properties())
                            {
                                string columnName = property.Name;
                                
                                // 跳过保留列名
                                if (reservedColumns.Contains(columnName))
                                    continue;

                                // 记录字段类型
                                if (!fieldTypes.ContainsKey(columnName))
                                {
                                    fieldTypes[columnName] = property.Value.Type;
                                }

                                // 如果是字符串类型，记录最大长度
                                if (property.Value.Type == JTokenType.String)
                                {
                                    int length = property.Value.ToString().Length;
                                    if (!fieldMaxLengths.ContainsKey(columnName) || fieldMaxLengths[columnName] < length)
                                    {
                                        fieldMaxLengths[columnName] = length;
                                    }
                                }
                            }
                        }
                    }

                    // 根据收集到的信息创建列
                    foreach (var kvp in fieldTypes)
                    {
                        string columnName = kvp.Key;
                        JTokenType tokenType = kvp.Value;

                        // 如果字段名是id，映射为original_id以避免与主键冲突
                        if (columnName.Equals("id", StringComparison.OrdinalIgnoreCase))
                        {
                            columnName = "original_id";
                        }

                        string columnType = GetColumnTypeWithLength(tokenType, fieldMaxLengths.ContainsKey(kvp.Key) ? fieldMaxLengths[kvp.Key] : 0);
                        columns.Add($"`{columnName}` {columnType}");
                    }

                    string createTableSql = $"CREATE TABLE `{tableName}` ({string.Join(", ", columns)}) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
                    MySqlCommand createCmd = new MySqlCommand(createTableSql, conn);
                    createCmd.ExecuteNonQuery();
                    WriteLog($"表 {tableName} 创建成功");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"创建表 {tableName} 失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 根据JSON值类型和最大长度确定MySQL列类型
        /// </summary>
        private string GetColumnTypeWithLength(JTokenType tokenType, int maxLength)
        {
            switch (tokenType)
            {
                case JTokenType.Integer:
                    return "BIGINT";
                case JTokenType.Float:
                    return "DECIMAL(18,2)";
                case JTokenType.Boolean:
                    return "BOOLEAN";
                case JTokenType.Date:
                    return "DATETIME";
                case JTokenType.String:
                    // 根据最大长度决定列类型
                    if (maxLength > 400 || maxLength == 0) // 如果超过400或未知长度，使用TEXT
                        return "TEXT";
                    else if (maxLength > 255)
                        return "VARCHAR(1000)"; // 给一些缓冲空间
                    else
                        return $"VARCHAR({Math.Max(maxLength * 2, 500)})"; // 给一些缓冲空间
                default:
                    return "TEXT";
            }
        }

        /// <summary>
        /// 内部方法：创建表（使用已有连接）
        /// </summary>
        private void CreateTableIfNotExistsInternal(MySqlConnection conn, string tableName, JObject sampleData)
        {
            try
            {
                // 检查表是否存在
                string checkTableSql = $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{tableName}'";
                MySqlCommand checkCmd = new MySqlCommand(checkTableSql, conn);
                int tableExists = Convert.ToInt32(checkCmd.ExecuteScalar());

                if (tableExists == 0)
                {
                    // 创建表
                    List<string> columns = new List<string>();
                    columns.Add("id INT AUTO_INCREMENT PRIMARY KEY");
                    columns.Add("created_at DATETIME DEFAULT CURRENT_TIMESTAMP");
                    columns.Add("updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP");

                    // 固定的列名，避免重复添加
                    HashSet<string> reservedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "id", "created_at", "updated_at"
                    };

                    // 根据JSON数据添加列（跳过已存在的固定列，将id映射为original_id）
                    foreach (var property in sampleData.Properties())
                    {
                        string columnName = property.Name;
                        
                        // 如果字段名是id，映射为original_id以避免与主键冲突
                        if (columnName.Equals("id", StringComparison.OrdinalIgnoreCase))
                        {
                            columnName = "original_id";
                        }
                        // 跳过其他已存在的固定列
                        else if (reservedColumns.Contains(columnName))
                        {
                            continue;
                        }
                        
                        string columnType = GetColumnType(property.Value);
                        columns.Add($"`{columnName}` {columnType}");
                    }

                    string createTableSql = $"CREATE TABLE `{tableName}` ({string.Join(", ", columns)}) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci";
                    MySqlCommand createCmd = new MySqlCommand(createTableSql, conn);
                    createCmd.ExecuteNonQuery();
                    WriteLog($"表 {tableName} 创建成功");
                }
            }
            catch (Exception ex)
            {
                WriteLog($"创建表 {tableName} 失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 内部方法：插入数据（使用已有连接）
        /// 返回true表示成功插入，false表示跳过（重复数据）
        /// </summary>
        private bool InsertDataInternal(MySqlConnection conn, string tableName, JObject data)
        {
            try
            {
                // 获取表的实际列名（用于检查id字段的实际列名）
                HashSet<string> tableColumns = GetTableColumns(conn, tableName);

                List<string> columns = new List<string>();
                List<string> values = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();

                // 固定的列名，这些字段是自动生成的，不需要插入
                HashSet<string> reservedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "id", "created_at", "updated_at"
                };

                foreach (var property in data.Properties())
                {
                    string columnName = property.Name;
                    string actualColumnName = columnName;
                    
                    // 如果字段名是id，需要检查表实际使用的是哪个列名
                    if (columnName.Equals("id", StringComparison.OrdinalIgnoreCase))
                    {
                        // 检查表是否有original_id列，如果有则使用original_id，否则使用id
                        if (tableColumns.Contains("original_id"))
                        {
                            actualColumnName = "original_id";
                        }
                        else if (tableColumns.Contains("id"))
                        {
                            // 如果表有id列（可能是之前创建的），也需要检查是否是主键
                            // 如果是主键，跳过；如果不是主键，使用id
                            // 这里我们假设如果表有id列，就使用它（可能是非主键的业务id列）
                            actualColumnName = "id";
                        }
                        else
                        {
                            // 如果表既没有original_id也没有id列，默认使用original_id
                            actualColumnName = "original_id";
                        }
                    }
                    // 跳过其他自动生成的字段（created_at, updated_at）
                    else if (reservedColumns.Contains(columnName))
                    {
                        continue;
                    }
                    
                    // 检查列是否存在（兼容大小写）
                    bool columnExists = false;
                    foreach (string col in tableColumns)
                    {
                        if (col.Equals(actualColumnName, StringComparison.OrdinalIgnoreCase))
                        {
                            columnExists = true;
                            actualColumnName = col; // 使用实际的列名（保持大小写）
                            break;
                        }
                    }
                    
                    // 如果列不存在，跳过该字段
                    if (!columnExists)
                    {
                        WriteLog($"警告：表 {tableName} 中不存在列 {actualColumnName}，跳过该字段");
                        // 如果所有列都不存在，记录未执行的数据
                        // 注意：这里只记录跳过的字段，不记录整个数据，因为可能还有其他字段可以插入
                        continue;
                    }
                    
                    columns.Add($"`{actualColumnName}`");
                    
                    string paramName = $"@param{parameters.Count}";
                    values.Add(paramName);
                    
                    MySqlParameter param = new MySqlParameter(paramName, GetMySqlDbType(property.Value.Type));
                    param.Value = GetValue(property.Value);
                    parameters.Add(param);
                }

                if (columns.Count == 0)
                {
                    WriteLog($"警告：没有可插入的列，数据: {data.ToString()}");
                    // 记录未执行的数据
                    WriteSkippedLog(tableName, data, "没有可插入的列");
                    return false; // 返回false表示跳过
                }

                // 使用 INSERT IGNORE 来处理重复键，如果数据已存在则跳过
                string insertSql = $"INSERT IGNORE INTO `{tableName}` ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
                MySqlCommand cmd = new MySqlCommand(insertSql, conn);
                cmd.Parameters.AddRange(parameters.ToArray());
                int affectedRows = cmd.ExecuteNonQuery();
                
                // 如果受影响的行数为0，说明数据已存在（被IGNORE忽略）
                if (affectedRows == 0)
                {
                    // 将重复数据记录到专门的日志文件
                    WriteDuplicateLog(tableName, data);
                    return false; // 返回false表示跳过
                }
                return true; // 返回true表示成功插入
            }
            catch (MySql.Data.MySqlClient.MySqlException mysqlEx)
            {
                // 如果是主键冲突错误，记录日志但不抛出异常
                if (mysqlEx.Number == 1062) // 1062 = Duplicate entry
                {
                    // 将重复数据记录到专门的日志文件
                    WriteDuplicateLog(tableName, data);
                    return false; // 返回false表示跳过
                }
                // 其他MySQL错误，抛出异常
                WriteLog($"插入数据到 {tableName} 失败: {mysqlEx.Message}");
                WriteLog($"数据: {data.ToString()}");
                throw;
            }
            catch (Exception ex)
            {
                WriteLog($"插入数据到 {tableName} 失败: {ex.Message}");
                WriteLog($"数据: {data.ToString()}");
                throw;
            }
        }

        /// <summary>
        /// 获取表的列名列表
        /// </summary>
        private HashSet<string> GetTableColumns(MySqlConnection conn, string tableName)
        {
            HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string sql = $"SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableName}'";
                MySqlCommand cmd = new MySqlCommand(sql, conn);
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string columnName = reader.GetString(0);
                        columns.Add(columnName);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"获取表 {tableName} 的列名失败: {ex.Message}");
            }
            return columns;
        }

        /// <summary>
        /// 获取MySQL数据类型
        /// </summary>
        private MySqlDbType GetMySqlDbType(JTokenType tokenType)
        {
            switch (tokenType)
            {
                case JTokenType.Integer:
                    return MySqlDbType.Int64;
                case JTokenType.Float:
                    return MySqlDbType.Decimal;
                case JTokenType.Boolean:
                    return MySqlDbType.Byte;
                case JTokenType.Date:
                    return MySqlDbType.DateTime;
                case JTokenType.String:
                    return MySqlDbType.VarChar;
                default:
                    return MySqlDbType.Text;
            }
        }

        /// <summary>
        /// 获取值
        /// </summary>
        private object GetValue(JToken token)
        {
            if (token.Type == JTokenType.Null)
                return DBNull.Value;

            switch (token.Type)
            {
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.Float:
                    return token.Value<decimal>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Date:
                    return token.Value<DateTime>();
                case JTokenType.String:
                    return token.Value<string>();
                default:
                    return token.ToString();
            }
        }

        /// <summary>
        /// 写入重复数据日志
        /// </summary>
        private static void WriteDuplicateLog(string tableName, JObject data)
        {
            lock (duplicateLogLock)
            {
                try
                {
                    string path = AppDomain.CurrentDomain.BaseDirectory;
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }

                    string duplicateLogFile = Path.Combine(path, "duplicate_log.txt");
                    string logMessage = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] 表: {tableName}, 重复数据: {data.ToString(Newtonsoft.Json.Formatting.None)}";
                    File.AppendAllText(duplicateLogFile, logMessage + "\r\n", Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    // 如果写入重复日志失败，记录到主日志
                    WriteLog($"写入重复数据日志失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 写入未执行数据日志
        /// </summary>
        public static void WriteSkippedLog(string tableName, JObject data, string reason)
        {
            lock (duplicateLogLock)
            {
                try
                {
                    string path = AppDomain.CurrentDomain.BaseDirectory;
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }

                    string skippedLogFile = Path.Combine(path, "skipped_log.txt");
                    string logMessage = $"[{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}] 表: {tableName}, 原因: {reason}, 数据: {data.ToString(Newtonsoft.Json.Formatting.None)}";
                    File.AppendAllText(skippedLogFile, logMessage + "\r\n", Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    // 如果写入跳过日志失败，记录到主日志
                    WriteLog($"写入未执行数据日志失败: {ex.Message}");
                }
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

