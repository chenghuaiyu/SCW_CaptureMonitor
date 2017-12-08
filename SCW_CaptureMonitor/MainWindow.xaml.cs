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
using log4net;

[assembly: log4net.Config.XmlConfigurator(Watch = true)]
namespace SCW_CaptureMonitor
{
    public delegate void RefreshVariableDelegate(string data_text, DateTime timeCurrent);

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        string appKey = "ClientApp";
        string httpPrefixKey = "HttpPrefix";
        string timeSpanMultipleKey = "TimeSpanMultiple";
        string elevatePrivilegeKey = "ElevatePrivilege";

        //{"intervalSecond" :10, "deviceNo": "chy", "ackType" :"", "feedback" :""}
        string httpIntervalSecondKey = "intervalSecond";
        string httpDeviceNoKey = "deviceNo";

        string appFilename;
        string appFileNameWithoutExtension;

        HttpRequestHandler handler = new HttpRequestHandler();
        Thread monitorThread = null;

        //KeyValuePair<string, DateTime> deviceLastTimes = new KeyValuePair<string,DateTime>();
        DateTime deviceLastTime = DateTime.Now; // new DateTime();
        int timeInterval = 0;
        int timeSpanMultiple = 2;
        bool elevatePrivilege = false;
        bool monitoring = true;
        long requestCount = 0;
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            log.Info("Application Starting");
            appFilename = ConfigurationManager.AppSettings[appKey];
            log.Info("client app: " + appFilename);
            if (!System.IO.File.Exists(appFilename))
            {
                log.Error("program exit due to the path not found: " + appFilename);
                MessageBox.Show("program exit due to the path not found: " + appFilename);
                Environment.Exit(0);
            }
            TextBoxProgram.Text = appFilename;
            appFileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(appFilename);

            monitorThread = new Thread(new ParameterizedThreadStart(MonitorThread));
            monitorThread.Start();

            Process[] proc = Process.GetProcessesByName(appFileNameWithoutExtension);
            if (proc.Count() == 0)
            {
                StartProcess(appFilename, "");
            }

            bool.TryParse(ConfigurationManager.AppSettings[elevatePrivilegeKey], out elevatePrivilege);

            string prefix = ConfigurationManager.AppSettings[httpPrefixKey];
            if (null == prefix)
            {
                log.Error("program exit due to HttpListener prefixes not found: " + httpPrefixKey);
                MessageBox.Show("program exit due to HttpListener prefixes not found: " + httpPrefixKey);
                Environment.Exit(0);
            }
            string[] prefixes = prefix.Split(';');
            foreach (string s in prefixes)
            {
                TextBoxHttp.Text += s + "\r\n";
                if (elevatePrivilege)
                {
                    AddAddressToAcl(s, "", "Everyone");
                }
            }

            int.TryParse(ConfigurationManager.AppSettings[timeSpanMultipleKey], out timeSpanMultiple);
            if (0 >= timeSpanMultiple)
            {
                timeSpanMultiple = 2;
            }
            TextBoxTimes.Text = timeSpanMultiple.ToString();

            handler.ListenAsynchronously(prefixes, RefreshVariables);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            log.Info("Application closing");
            monitoring = false;
            Thread.Sleep(1000);
            if (null != monitorThread)
            {
                monitorThread.Abort();
            }
            if (null != handler)
            {
                handler.StopListening();
            }
        }

        public static void AddAddressToAcl(string address, string domain, string user)
        {
            string args = string.Format(@"http add urlacl url={0} user={1}\{2}", address, domain, user);

            ProcessStartInfo startInfo = new ProcessStartInfo("netsh", args);
            startInfo.Verb = "runas";
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.UseShellExecute = true;

            Process.Start(startInfo).WaitForExit();
        }

        public void RefreshVariables(string data_text, DateTime timeCurrent)
        {
            var cleaned_data = System.Web.HttpUtility.UrlDecode(data_text);

            // Parse the results into a JSON object
            JObject json = JObject.Parse(cleaned_data);
            if (0 == timeInterval)
            {
                int interval = json[httpIntervalSecondKey].ToObject<int>();
                var device = json[httpDeviceNoKey].ToString();
                timeInterval = interval < 1 ? 1 : interval;
                //TextBoxClient.Text = "start to listen: " + DateTime.Now.ToString();
            }
            log.Debug("request id: " + requestCount.ToString() + ", last time: " + deviceLastTime.ToString() + ", current: " + timeCurrent.ToString());
            deviceLastTime = timeCurrent;

            requestCount++;
        }

        private void MonitorThread(object s)
        {
            log.Info("start to monitor http request.");
            while (monitoring)
            {
                if (timeInterval > 0)
                {
                    TimeSpan ts = DateTime.Now - deviceLastTime;
                    TimeSpan tsThresh = new TimeSpan(0, 0, timeInterval * timeSpanMultiple);
                    if (timeInterval > 0 && ts > tsThresh)
                    {
                        log.Info("time span: " + ts.ToString() + " > threshold: " + tsThresh.ToString());
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
                else
                {
                    Thread.Sleep(30000);
                    Process[] proc = Process.GetProcessesByName(appFileNameWithoutExtension);
                    if (proc.Count() == 0)
                    {
                        Thread.Sleep(60000);
                        proc = Process.GetProcessesByName(appFileNameWithoutExtension);
                        if (proc.Count() == 0)
                        {
                            StartProcess(appFilename, "");
                        }
                    }
                }
            }
            log.Info("end the http request monitoring.");
        }

        public static void StartProcess(string appFilename, string args)
        {
            try
            {
                log.Info("start process: " + appFilename + ", with arguments: " + args);
                ProcessStartInfo startInfo = new ProcessStartInfo(appFilename, args);
                startInfo.WorkingDirectory = System.IO.Path.GetDirectoryName(appFilename);

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                log.Warn("start process exception: " + ex.Message + ", StackTrace: " + ex.StackTrace);
            }
        }

        bool KillProcess(string processName)
        {
            try
            {
                log.Info("find process  to kill: " + processName);
                Process[] proc = Process.GetProcessesByName(processName);
                if (proc.Count() > 0)
                {
                    log.Info("kill process: " + proc[0].ProcessName);
                    proc[0].Kill();
                    if (proc.Count() > 1)
                    {
                        string message = proc.Count() + " processes found which name include \"" + processName + "\", but just the first one should be terminated.";
                        log.Warn(message);
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                log.Warn("kill process exception: " + ex.Message + ", StackTrace: " + ex.StackTrace);
            }
            return false;
        }
    }
}