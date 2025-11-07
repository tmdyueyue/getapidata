using System;
using System.Collections.Generic;
using static System.Net.WebRequestMethods;

namespace U_up
{
    /// <summary>
    /// 配置类 - 存储接口URL和MySQL连接信息
    /// </summary>
    public class Config
    {
        /// <summary>
        /// MySQL连接字符串
        /// 格式: Server=localhost;Database=dbname;Uid=username;Pwd=password;CharSet=utf8mb4;
        /// </summary>
        public static string MySqlConnectionString { get; set; } = "Server=localhost;Database=grabdata;Uid=root;Pwd=EdCu^eeGf5ixZ226:.Q>8M;CharSet=utf8mb4;";

        /// <summary>
        /// 接口配置列表
        /// 每个接口包含: URL模板, 分页参数名, 表名, 数据字段映射
        /// </summary>
        public static List<ApiConfig> ApiConfigs { get; set; } = new List<ApiConfig>
        {
          
                   new ApiConfig
            {
                ApiName = "数据读取",
                UrlTemplate = "https://rygl-api.szctkj.net:9006/V2/hs/construct/plan/pageList",
                PageParamName = "pageNo", // 页码参数名
                PageSizeParamName = "pageSize", // 每页数量参数名
                PageSize = 10,
                TableName = "construct", //  表名
                DataPath = "data.records", // 需要根据实际返回结构调整
                TotalFieldName = "data.total", // 需要根据实际返回结构调整
                Authorization = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyaWQiOiI3MDMyNjU2OTUxMzc4NjEiLCJ1c2VybmFtZSI6IkFQSSIsIm9yZ2FuaXphdGlvbmlkIjoiNjg0MTgwOTA1NTUzOTg5IiwibmJmIjoxNzUzOTQ2MjkzLCJleHAiOjE3NTM5NDk4OTN9.rqKfQbyP7nf4DQanJ4ioAr89jJ_rBikqaPgRQMU92W4",
                HttpMethod = "POST",
               // PostBodyTemplate = "{{\"currPage\":{0},\"size\":{1},\"total\":0,\"keyWord\":\"\",\"entity\":{{\"gqId\":null,\"companyId\":null,\"statusArr\":[\"1\",\"2\",\"3\",\"7\"],\"idCardNumber\":\"\",\"mobile\":\"\",\"projectId\":{2},\"teamId\":null,\"isTsGz\":0,\"pictureType\":\"0\",\"hasContract\":\"\",\"sdate\":\"\"}},\"pictureType\":\"0\",\"hasContract\":\"\",\"statusArr\":[\"1\",\"2\",\"3\",\"7\"],\"isTsGz\":0,\"sdate\":\"\"}}", // 包含projectId参数，{2} 会被替换为projectId
                //PostBodyTemplate = "{{\"pageNo\":{0},\"pageSize\":{1},\"projectId\":{2}}}", // 包含projectId参数，{2} 会被替换为projectId
                PostBodyTemplate = "{{\"pageNo\":{0},\"pageSize\":{1},\"scheduleDate\":null,\"gqId\":null,\"projectId\":{2}}}", // 包含projectId参数，{2} 会被替换为projectId

  



                NeedProjectIdLoop = true, // 启用projectId循环
                ProjectIdStart = 1, // projectId起始值
                ProjectIdEnd = 105 // projectId结束值（会循环1到120）
            }
         
        };
    }

    /// <summary>
    /// 接口配置类
    /// </summary>
    public class ApiConfig
    {
        /// <summary>
        /// 接口名称
        /// </summary>
        public string ApiName { get; set; }

        /// <summary>
        /// URL模板，{0} 为页码，{1} 为每页数量
        /// </summary>
        public string UrlTemplate { get; set; }

        /// <summary>
        /// 分页参数名
        /// </summary>
        public string PageParamName { get; set; } = "page";

        /// <summary>
        /// 每页数量参数名
        /// </summary>
        public string PageSizeParamName { get; set; } = "pageSize";

        /// <summary>
        /// 每页数量
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 数据库表名
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// JSON数据路径，如 "data.items" 或 "items"，用于提取数据数组
        /// </summary>
        public string DataPath { get; set; }

        /// <summary>
        /// 总数字段名，用于判断是否还有更多页，如 "total" 或 "data.total"
        /// </summary>
        public string TotalFieldName { get; set; } = "total";

        /// <summary>
        /// 是否需要登录（如果需要，会在请求时使用CookieContainer）
        /// </summary>
        public bool RequireLogin { get; set; } = false;

        /// <summary>
        /// 登录URL（如果需要登录）
        /// </summary>
        public string LoginUrl { get; set; }

        /// <summary>
        /// 登录数据（如果需要登录）
        /// </summary>
        public string LoginData { get; set; }

        /// <summary>
        /// Authorization 头，用于API身份认证（如 Bearer token）
        /// 格式: "Bearer token" 或 "token"
        /// </summary>
        public string Authorization { get; set; }

        /// <summary>
        /// HTTP请求方法，默认为GET
        /// </summary>
        public string HttpMethod { get; set; } = "GET";

        /// <summary>
        /// POST请求体模板（仅当HttpMethod为POST时使用）
        /// {0} 为页码，{1} 为每页数量，{2} 为projectId（如果使用）
        /// 示例: "{\"page\":{0},\"pageSize\":{1}}"
        /// 或: "{{\"currPage\":{0},\"size\":{1},\"total\":0,\"entity\":{{\"projectId\":{2}}}}}"
        /// </summary>
        public string PostBodyTemplate { get; set; }

        /// <summary>
        /// 是否需要循环projectId
        /// </summary>
        public bool NeedProjectIdLoop { get; set; } = false;

        /// <summary>
        /// ProjectId列表（如果需要循环projectId）
        /// 如果为空，则从ProjectIdStart到ProjectIdEnd循环
        /// </summary>
        public List<int> ProjectIdList { get; set; }

        /// <summary>
        /// ProjectId起始值（如果需要循环projectId）
        /// </summary>
        public int ProjectIdStart { get; set; } = 1;

        /// <summary>
        /// ProjectId结束值（如果需要循环projectId）
        /// </summary>
        public int ProjectIdEnd { get; set; } = 120;
    }
}

