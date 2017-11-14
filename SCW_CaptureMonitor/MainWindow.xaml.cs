using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SCW_CaptureMonitor
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        string appKey = "ClientApp";
        string portKey = "Port";

        //{"intervalSecond" :10, "deviceNo": "chy", "ackType" :"", "feedback" :""}
        string httpIntervalSecondKey = "intervalSecond";
        string httpDeviceNoKey = "deviceNo";

        string appFilename;
        string appFileNameWithoutExtension;

        HttpListener listener = null;
        Thread listenThread1 = null;
        Thread monitorThread = null;

        //KeyValuePair<string, DateTime> deviceLastTimes = new KeyValuePair<string,DateTime>();
        DateTime deviceLastTime = new DateTime();
        int timeInterval = 0;
        int timeSpanMultiple = 4;
        bool monitoring = true;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            int port = 1123;
            int.TryParse(System.Configuration.ConfigurationManager.AppSettings[portKey], out port);

            //"http://localhost:1123/captureCard/heartbeat"
            List<string> prefixes = new List<string>();
            prefixes.Add("http://localhost:" + port + "/");
            prefixes.Add("http://127.0.0.1:" + port + "/");

            appFilename = ConfigurationManager.AppSettings[appKey];
            appFileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(appFilename);

            monitorThread = new Thread(new ParameterizedThreadStart(MonitorThread));
            monitorThread.Start();

            Process[] proc = Process.GetProcessesByName(appFileNameWithoutExtension);
            if (proc.Count() == 0)
            {
                StartProcess(appFilename, "");
            }

            InvokeHttpListener(prefixes);
        }

        private void MonitorThread(object s)
        {
            while (monitoring)
            {
                if (timeInterval > 0)
                {
                    TimeSpan ts = DateTime.Now - deviceLastTime;
                    TimeSpan tsThresh = new TimeSpan(0, 0, timeInterval * timeSpanMultiple);
                    if (timeInterval > 0 && ts > tsThresh)
                    {
                        bool bRet = KillProcess(appFileNameWithoutExtension);
                        //if (bRet)
                        {
                            Thread.Sleep(2000);
                            deviceLastTime = new DateTime();
                            timeInterval = 0;
                            StartProcess(appFilename, "");
                            Thread.Sleep(timeInterval * timeSpanMultiple * 1000);
                        }
                    }
                    else
                    {
                        //timeInterval = 0;
                    }
                }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            monitoring = false;
            Thread.Sleep(1000);
            if (null != monitorThread)
            {
                monitorThread.Abort();
            }
            if (null != listener)
            {
                listenThread1.Abort();
                listener.Stop();
                listener.Close();
                Thread.Sleep(1000);
            }
        }

        public void InvokeHttpListener(List<string> prefixes)
        {
            if (!HttpListener.IsSupported)
            {
                Console.WriteLine("Windows XP SP2 or Server 2003 is required to use the HttpListener class.");
                return;
            }
            // URI prefixes are required,
            // for example "http://contoso.com:8080/index/".
            if (prefixes == null || prefixes.Count == 0)
            {
                throw new ArgumentException("prefixes");
            }

            // Create a listener.
            listener = new HttpListener();
            // Add the prefixes.
            foreach (string s in prefixes)
            {
                listener.Prefixes.Add(s);
            }
            listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
            listener.Start();

            listenThread1 = new Thread(new ParameterizedThreadStart(startlistener));
            listenThread1.Start();
        }

        private void startlistener(object s)
        {
            while (monitoring)
            {
                //blocks until a client has connected to the server
                ProcessRequest();
            }
        }

        private void ProcessRequest()
        {
            try
            {
                var result = listener.BeginGetContext(ListenerCallback, listener);
                result.AsyncWaitHandle.WaitOne();
            }
            catch (Exception)
            {
                //throw;
            }
        }

        private void ListenerCallback(IAsyncResult result)
        {
            DateTime timeCurrent = DateTime.Now;
            var context = listener.EndGetContext(result);
            //Thread.Sleep(1000);
            var data_text = new StreamReader(context.Request.InputStream,
            context.Request.ContentEncoding).ReadToEnd();

            context.Response.StatusCode = 200;
            context.Response.StatusDescription = "OK";

            //use this line to get your custom header data in the request.
            //var headerText = context.Request.Headers["mycustomHeader"];

            //use this line to send your response in a custom header
            //context.Response.Headers["mycustomResponseHeader"] = "mycustomResponse";
            context.Response.Close();

            var cleaned_data = System.Web.HttpUtility.UrlDecode(data_text);
            //MessageBox.Show(cleaned_data);

            // Parse the results into a JSON object
            JObject json = JObject.Parse(cleaned_data);
            if (0 == timeInterval)
            {
                int interval = json[httpIntervalSecondKey].ToObject<int>();
                var device = json[httpDeviceNoKey].ToString();
                timeInterval = interval < 1 ? 1 : interval;
            }
            deviceLastTime = timeCurrent;

            ////functions used to decode json encoded data.
            //JavaScriptSerializer js = new JavaScriptSerializer();
            //var data1 = Uri.UnescapeDataString(data_text);
            ////string da = Regex.Unescape(data_text);
            //var unserialized = js.Deserialize(data_text, typeof(String));

            //JObject json = JObject.Parse(unserialized);
        }

        public static void StartProcess(string appFilename, string args)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(appFilename, args);
                startInfo.WorkingDirectory = System.IO.Path.GetDirectoryName(appFilename);

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace, "error");
            }
        }

        bool KillProcess(string processName)
        {
            try
            {
                Process[] proc = Process.GetProcessesByName(processName);
                if (proc.Count() > 0)
                {
                    proc[0].Kill();
                    if (proc.Count() > 1)
                    {
                        string message = proc.Count() + " processes found which name include \"" + processName + "\", but just the first one should be terminated.";
                    }
                    return true;
                }
            }
            catch { }
            return false;

        }
    }
}