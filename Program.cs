using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace U_up
{
 
     public  class Program
    {
        System.Net.HttpWebResponse responsett;
        CookieContainer Cookiee = new CookieContainer();
        static void Main(string[] args)
        {
            Console.WriteLine("服务开启中请勿关闭!");
            new Program().job();

        }

        public void job()
        {
            string OrgId = "7A388010-CB24-F5A8-2DB9-A0257CB414AD";  //公司编号
            //登录 _> 查询是否可以签到 -> 签到 -> 退出        服务由定时器调用
            string Login = "https://web.iusung.com/api/App/User/Login";      //登录
            string GetMySignOrder = "https://web.iusung.com/api/App/EpSign/GetMySignOrder";  //签到检查
            string Sign = "https://web.iusung.com/api/App/EpSign/Sign";  //签到

            List<Mobile> xx = new List<Mobile>();

            //decimal Lat = 29.060151M;      //常烟纬度
            //decimal Lng = 111.650121M;     //常烟经度


            decimal Lat = 28.156812M;      //长沙公司纬度
            decimal Lng = 113.031897M;     //长沙公司经度

            Mobile zx = new Mobile();
            zx.MobilePhone = "18163712912";
            zx.Password = "4ef32e1736d93e5dd169bb4fec1169d8";
            zx.SendKey = "SCT289642TQiY5CSktLBt8jzQVcbFXfk9y";
            zx.Lat = Lat;
            zx.Lng = Lng;
            xx.Add(zx);

            Mobile qb = new Mobile();
            qb.MobilePhone = "17307491838";
            qb.Password = "ba26481e4f997e347aa0dac54ef91927";
            qb.SendKey = "SCT289644TGmffrhKVAXcPzt3mReofTUMs";
            qb.Lat = Lat;
            qb.Lng = Lng;
            xx.Add(qb);

            Mobile zp = new Mobile();
            zp.MobilePhone = "18670033173";
            zp.Password = "6c5cb25dd8055d9b741f54679a7bdb70";
            zp.SendKey = "SCT207740TY3KEIiHwAPqYzdNqn7RhFRQ9";
            zp.Lat = Lat;
            zp.Lng = Lng;
            xx.Add(zp);

            Mobile dl = new Mobile();
            dl.MobilePhone = "18570647721";
            dl.Password = "0a4e82dee78fed73460891f7ca309288";
            dl.SendKey = "SCT289643TeIVEbvq86hnrs5C7ooSH8noe";
            dl.Lat = Lat;
            dl.Lng = Lng;
            xx.Add(dl);

            Mobile zmz = new Mobile();
            zmz.MobilePhone = "13755189028";
            zmz.Password = "25d55ad283aa400af464c76d713c07ad";
            zmz.SendKey = "SCT289641TRFEK33lN5grPdKyjkx146AMT";
            zmz.Lat = Lat;
            zmz.Lng = Lng;
            xx.Add(zmz);


            Random rng = new Random();
            int n = xx.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                Mobile value = xx[k];
                xx[k] = xx[n];
                xx[n] = value;
            }

           


            try
            {
                foreach (Mobile item in xx)
                {
                    Random random = new Random();


                    // 生成100到9999之间的随机数
                    int randomNumber = random.Next(100, 10000);
                    int randomNumber1 = random.Next(100, 10000);
                    // 将Lng转换为保留两位小数的数值
                    decimal newLng = Math.Floor(item.Lng * 100) / 100;
                    decimal newLat = Math.Floor(item.Lat * 100) / 100;
                    // 将随机数除以10000并加到newLng上
                    newLng += randomNumber / 1000000.0M;
                    newLat += randomNumber1 / 1000000.0M;
                    item.Lng = newLng;
                    item.Lat = newLat;

                    int TIME = random.Next(10000, 240000);         //2分钟内随机延时,延时后执行签到

                    Thread.Sleep(TIME);
                    //登录,不登录调用不了签到
                    string dataJSON = "{\"MobilePhone\":\"" + item.MobilePhone + "\",\"Password\":\"" + item.Password + "\",\"UsClientId\":\"" + item.UsClientId + "\",\"Version\":\"" + item.Version + "\"}";
                    System.Net.HttpWebRequest request;
                    request = (System.Net.HttpWebRequest)WebRequest.Create(Login);
                    request.Method = "POST";
                    request.ContentType = "application/json;charset=UTF-8";
                    request.CookieContainer = Cookiee;
                    string paraUrlCoded = dataJSON;
                    byte[] payload;
                    payload = System.Text.Encoding.UTF8.GetBytes(paraUrlCoded);
                    request.ContentLength = payload.Length;
                    Stream writer = request.GetRequestStream();
                    writer.Write(payload, 0, payload.Length);
                    writer.Close();
                    responsett = (System.Net.HttpWebResponse)request.GetResponse();
                    foreach (Cookie ck in responsett.Cookies)
                    {
                        Cookiee.Add(ck);
                    }
                    System.IO.Stream s;
                    s = responsett.GetResponseStream();
                    string StrDate = "";
                    string strValue = "";
                    StreamReader Reader = new StreamReader(s, Encoding.UTF8);
                    while ((StrDate = Reader.ReadLine()) != null)
                    {
                        strValue += StrDate + "\r\n";
                    }
                    string json = strValue;
                    JObject ob = JObject.Parse(json);

                    string resNsg = ob["msg"].ToString();
                    if (resNsg == "SUCCESS")
                    {
                        json = HttpGet(GetMySignOrder);
                        json = json.Replace("[", "");
                        json = json.Replace("]", "");
                        JObject SignOrder = Newtonsoft.Json.Linq.JObject.Parse(json);
                        resNsg = SignOrder["msg"].ToString();
                        if (resNsg == "SUCCESS"&& (int)SignOrder["total"] > 0)
                        {
                     
                            string OrderId = SignOrder["items"]["OrderId"].ToString();
                            dataJSON = "{\"OrderIds\":[\"" + OrderId + "\"],\"Coord\":{\"Lat\":" + item.Lat + ",\"Lng\":" + item.Lng + "},\"DeviceId\":\"\",\"DeviceName\":\"\"}";
                            string x = Httppost(Sign, dataJSON);
                            WriteLog("ok");

                            string logname = "签到成功";
                            string messageContent = $"手机号: {item.MobilePhone}\n时间: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}\n状态: 签到成功";
                            

                            // URL编码处理中文和特殊字符
                            string encodedTitle = System.Web.HttpUtility.UrlEncode(logname);
                            string encodedContent = System.Web.HttpUtility.UrlEncode(messageContent);
                            string sendKey = System.Web.HttpUtility.UrlEncode(item.SendKey);


                            string url = $"https://sctapi.ftqq.com/{sendKey}.send?title={encodedTitle}&desp={encodedContent}";
                            Console.WriteLine(url);
                            HttpGet(url);
                        }
                        else
                        {
                            WriteLog("没有任务");
                        }
                    }
                    else
                    {
                        WriteLog("密码错咯");
                    }
                }
            }
            catch (Exception e)
            {
                WriteLog(e.Message);
                
                // 发送签到失败通知
                try
                {
                    string errorTitle = "签到失败";
                    string errorMessage = $"发生错误: {e.Message}";
                    
                    // 限制错误信息长度，避免URL过长
                    if (errorMessage.Length > 200)
                    {
                        errorMessage = errorMessage.Substring(0, 200) + "...";
                    }
                    
                    // URL编码处理
                    string encodedErrorTitle = System.Web.HttpUtility.UrlEncode(errorTitle);
                    string encodedErrorMessage = System.Web.HttpUtility.UrlEncode(errorMessage);
                    
                    string errorUrl = $"https://sctapi.ftqq.com/SCT289642TQiY5CSktLBt8jzQVcbFXfk9y.send?title={encodedErrorTitle}&desp={encodedErrorMessage}";
                    Console.WriteLine("错误通知URL: " + errorUrl);
                    HttpGet(errorUrl);
                     errorUrl = $"https://sctapi.ftqq.com/SCT289644TGmffrhKVAXcPzt3mReofTUMs.send?title={encodedErrorTitle}&desp={encodedErrorMessage}";
                    Console.WriteLine("错误通知URL: " + errorUrl);
                    HttpGet(errorUrl);
                     errorUrl = $"https://sctapi.ftqq.com/SCT207740TY3KEIiHwAPqYzdNqn7RhFRQ9.send?title={encodedErrorTitle}&desp={encodedErrorMessage}";
                    Console.WriteLine("错误通知URL: " + errorUrl);
                    HttpGet(errorUrl);
                     errorUrl = $"https://sctapi.ftqq.com/SCT289643TeIVEbvq86hnrs5C7ooSH8noe.send?title={encodedErrorTitle}&desp={encodedErrorMessage}";
                    Console.WriteLine("错误通知URL: " + errorUrl);
                    HttpGet(errorUrl);

                     errorUrl = $"https://sctapi.ftqq.com/SCT289641TRFEK33lN5grPdKyjkx146AMT.send?title={encodedErrorTitle}&desp={encodedErrorMessage}";
                    Console.WriteLine("错误通知URL: " + errorUrl);
                    HttpGet(errorUrl);
                }
                catch (Exception notifyEx)
                {
                    WriteLog("发送错误通知失败: " + notifyEx.Message);
                }
                
                // 重新抛出异常
                throw;
            }
        }
        public static void WriteLog(string strLog)
        {

            StreamWriter stream;
            string path = AppDomain.CurrentDomain.BaseDirectory;

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
         
      
            stream = new StreamWriter(path + "\\log.txt", true, Encoding.Default);
            stream.Write(DateTime.Now.ToString() + ":" + strLog);
            stream.Write("\r\n");
            stream.Flush();
            stream.Close();
 
        }

        public string Httppost(string url,string dataJSON)
        {
            System.Net.HttpWebRequest request;
            request = (System.Net.HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json;charset=UTF-8";
            request.CookieContainer = Cookiee;
            string paraUrlCoded = dataJSON;
            byte[] payload;
            payload = System.Text.Encoding.UTF8.GetBytes(paraUrlCoded);
            request.ContentLength = payload.Length;
            Stream writer = request.GetRequestStream();
            writer.Write(payload, 0, payload.Length);
            writer.Close();
            responsett = (System.Net.HttpWebResponse)request.GetResponse();
            foreach (Cookie ck in responsett.Cookies)
            {
                Cookiee.Add(ck);
            }
            System.IO.Stream s;
            s = responsett.GetResponseStream();
            string StrDate = "";
            string strValue = "";
            StreamReader Reader = new StreamReader(s, Encoding.UTF8);
            while ((StrDate = Reader.ReadLine()) != null)
            {
                strValue += StrDate + "\r\n";
            }
            return strValue;

        }

        public string HttpGet(string url)
        {
            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "*/*";
            request.Timeout = 15000;
            request.CookieContainer = Cookiee;
            request.AllowAutoRedirect = false;
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
            catch (Exception)
            {
                throw;
            }
            finally
            {
                request = null;
                response = null;
            }
            return responseStr;
        }

    }

   

    internal class Mobile
    {
        public string MobilePhone { set; get; }
        public string Password { set; get; }
        public string UsClientId { set; get; }
        public string Version { set; get; }

        public string SendKey { set; get; }
        /// <summary>
        /// 纬度短的
        /// </summary>
        public decimal Lat { set; get; }
        /// <summary>
        /// 经度长的
        /// </summary>
        public decimal Lng { set; get; }


    }

    public class items

    {
        public string OrgId { get; set; }

        public string OrderId { get; set; }

    }

    public class ccx
    {
        public items items { get; set; }
        public string msg { get; set; }
        public int total { get; set; }
    }
}
