using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace SCW_CaptureMonitor
{
    public class HttpListenerCallbackState
    {
        private readonly HttpListener _listener;
        private readonly AutoResetEvent _listenForNextRequest;

        public HttpListenerCallbackState(HttpListener listener)
        {
            if (listener == null) throw new ArgumentNullException("listener");
            _listener = listener;
            _listenForNextRequest = new AutoResetEvent(false);
        }

        public HttpListener Listener { get { return _listener; } }
        public AutoResetEvent ListenForNextRequest { get { return _listenForNextRequest; } }
    }
    struct httpParam
    {
        public HttpListenerCallbackState state;
        public RefreshVariableDelegate rvd;
    }

    public class HttpRequestHandler
    {
        private int requestCounter = 0;
        private ManualResetEvent stopEvent = new ManualResetEvent(false);

        public void ListenAsynchronously(IEnumerable<string> prefixes, RefreshVariableDelegate rvd)
        {
            HttpListener listener = new HttpListener();
            try
            {
                foreach (string s in prefixes)
                {
                    listener.Prefixes.Add(s);
                }

                listener.Start();

            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace);
            }

            httpParam hp = new httpParam();
            hp.state = new HttpListenerCallbackState(listener);
            hp.rvd = rvd;
            ThreadPool.QueueUserWorkItem(Listen, hp);
        }

        public void StopListening()
        {
            stopEvent.Set();
        }

        private void Listen(object callbackPar)
        {
            httpParam hp = (httpParam)callbackPar;
            HttpListener hl = hp.state.Listener;

            while (hl.IsListening)
            {
                hl.BeginGetContext(new AsyncCallback(ListenerCallback), callbackPar);
                int n = WaitHandle.WaitAny(new WaitHandle[] { hp.state.ListenForNextRequest, stopEvent });
                if (n == 1)
                {
                    // stopEvent was signaled 
                    hl.Stop();
                    break;
                }
            }
        }

        private void ListenerCallback(IAsyncResult ar)
        {
            httpParam hp = (httpParam)ar.AsyncState;

            HttpListenerCallbackState callbackState = (HttpListenerCallbackState)hp.state;
            HttpListenerContext context = null;

            int requestNumber = Interlocked.Increment(ref requestCounter);

            try
            {
                context = callbackState.Listener.EndGetContext(ar);
            }
            catch (Exception ex)
            {
                return;
            }
            finally
            {
                callbackState.ListenForNextRequest.Set();
            }

            if (context == null) { return; }

            DateTime timeCurrent = DateTime.Now;

            HttpListenerRequest request = context.Request;

            if (request.HasEntityBody)
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string requestData = sr.ReadToEnd();

                    //Stuff I do with the request happens here
                    hp.rvd(requestData, timeCurrent);
                }
            }

            try
            {
                using (HttpListenerResponse response = context.Response)
                {
                    //response stuff happens here  
                    string responseString = "Ok";

                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                    response.ContentLength64 = buffer.LongLength;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                    response.Close();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message + "\r\n" + ex.StackTrace);
            }
        }
    }
}