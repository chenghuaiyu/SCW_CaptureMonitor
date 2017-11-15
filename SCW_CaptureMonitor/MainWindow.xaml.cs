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
    public delegate void RefreshVariableDelegate(string data_text, DateTime timeCurrent);

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        string appKey = "ClientApp";
        string httpPrefixKey = "HttpPrefix";
        string timeSpanMultipleKey = "TimeSpanMultiple";

        //{"intervalSecond" :10, "deviceNo": "chy", "ackType" :"", "feedback" :""}
        string httpIntervalSecondKey = "intervalSecond";
        string httpDeviceNoKey = "deviceNo";

        string appFilename;
        string appFileNameWithoutExtension;

        HttpRequestHandler handler = new HttpRequestHandler();
        Thread monitorThread = null;

        //KeyValuePair<string, DateTime> deviceLastTimes = new KeyValuePair<string,DateTime>();
        DateTime deviceLastTime = new DateTime();
        int timeInterval = 0;
        int timeSpanMultiple = 2;
        bool monitoring = true;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            appFilename = ConfigurationManager.AppSettings[appKey];
            if (!System.IO.File.Exists(appFilename))
            {
                MessageBox.Show("program path not found: " + appFilename);
                Environment.Exit(0);
            }
            appFileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(appFilename);

            monitorThread = new Thread(new ParameterizedThreadStart(MonitorThread));
            monitorThread.Start();

            Process[] proc = Process.GetProcessesByName(appFileNameWithoutExtension);
            if (proc.Count() == 0)
            {
                StartProcess(appFilename, "");
            }
            string prefix = ConfigurationManager.AppSettings[httpPrefixKey];
            if (null == prefix)
            {
                MessageBox.Show("HttpListener prefixes not found: " + httpPrefixKey);
                Environment.Exit(0);
            }
            string[] prefixes = prefix.Split(';');

            int.TryParse(ConfigurationManager.AppSettings[timeSpanMultipleKey], out timeSpanMultiple);
            if (0 >= timeSpanMultiple)
            {
                timeSpanMultiple = 2;
            }

            handler.ListenAsynchronously(prefixes, RefreshVariables);

            //InvokeHttpListener(prefixes);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
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
            }
            deviceLastTime = timeCurrent;
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