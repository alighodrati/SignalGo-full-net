﻿using Newtonsoft.Json;
using SignalGo.Shared.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace SignalGo.Shared.Log
{
    /// <summary>
    /// base of log information
    /// </summary>
    public abstract class BaseLogInformation
    {
        /// <summary>
        /// ignore log for this iformation
        /// </summary>
        public bool IsIgnoreLogTextFile { get; set; }
        /// <summary>
        /// date of call
        /// </summary>
        public DateTime DateTimeStartMethod { get; set; }
        /// <summary>
        /// date of result
        /// </summary>
        public DateTime DateTimeEndMethod { get; set; }
        /// <summary>
        /// Elapsed time from start call to result
        /// </summary>
        public TimeSpan Elapsed
        {
            get
            {
                return DateTimeEndMethod - DateTimeStartMethod;
            }
        }

        /// <summary>
        /// after set call method result is going to true and write to log file
        /// </summary>
        public bool CanWriteToFile { get; set; }
        /// <summary>
        /// client clientId
        /// </summary>
        public string ClientId { get; set; }
        /// <summary>
        /// client connected Date Time
        /// </summary>
        public DateTime ConnectedDateTime { get; set; }
        /// <summary>
        /// ip addresses
        /// </summary>
        public string IPAddress { get; set; }
        /// <summary>
        /// object od result
        /// </summary>
        public object Result { get; set; }
        /// <summary>
        /// name of method
        /// </summary>
        public string MethodName { get; set; }
        /// <summary>
        /// if method call have exception result
        /// </summary>
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// log of method called
    /// </summary>
    public class CallMethodLogInformation : BaseLogInformation
    {
        /// <summary>
        /// name of service
        /// </summary>
        public string ServiceName { get; set; }
        /// <summary>
        /// parameters of method
        /// </summary>
        public List<Models.ParameterInfo> Parameters { get; set; }
        /// <summary>
        /// method
        /// </summary>
        public MethodInfo Method { get; set; }
    }

    /// <summary>
    /// log of callbacks
    /// </summary>
    public class CallClientMethodLogInformation : BaseLogInformation
    {
        /// <summary>
        /// service name
        /// </summary>
        public string ServiceName { get; set; }
        /// <summary>
        /// parameters
        /// </summary>
        public List<Models.ParameterInfo> Parameters { get; set; }
    }

    /// <summary>
    /// log of http calls
    /// </summary>
    public class HttpCallMethodLogInformation : BaseLogInformation
    {
        /// <summary>
        /// address of http call
        /// </summary>
        public string Address { get; set; }
        /// <summary>
        /// parameters
        /// </summary>
        public List<string> Parameters { get; set; }
        /// <summary>
        /// method
        /// </summary>
        public MethodInfo Method { get; set; }
    }

    /// <summary>
    /// signalGo log system manager
    /// </summary>
    public static class MethodCallsLogger
    {
        /// <summary>
        /// when user called and response a service method
        /// </summary>
        public static Action<CallMethodLogInformation> OnServiceMethodCalledAction { get; set; }
        /// <summary>
        /// when server called a client method
        /// </summary>
        public static Action<CallClientMethodLogInformation> OnServiceCallbackMethodCalledAction { get; set; }
        /// <summary>
        /// when a http method called from client
        /// </summary>
        public static Action<HttpCallMethodLogInformation> OnHttpServiceMethodCalledAction { get; set; }

        /// <summary>
        /// if false ignore write errors to .log file
        /// </summary>
        public static bool IsEnabled
        {
            set
            {
                StopEngine();
                if (value)
                    StartEngine();
            }
        }
        /// <summary>
        /// when system logger is started
        /// </summary>
        public static bool IsStart
        {
            get
            {
                return !isStop;
            }
        }
        /// <summary>
        /// if you want log persian datet time
        /// </summary>
        public static bool IsPersianDateLog { get; set; } = false;

        static bool isStop = true;
        static void StopEngine()
        {
            if (isStop)
                return;
            waitForDispose.Reset();
            waitForDispose.WaitOne();
        }

        static ManualResetEvent waitForDispose = new ManualResetEvent(false);

        static void StartEngine()
        {
#if (PORTABLE)
            throw new NotSupportedException();
#else
            if (!isStop)
                return;
            isStop = false;
            Thread _thread = new Thread(() =>
            {
                try
                {
                    BaseLogInformation nextLog = null;
                    while (!isStop)
                    {
                        if (Logs.TryPeek(out nextLog) && nextLog.CanWriteToFile)
                        {
                            if (Logs.TryDequeue(out nextLog))
                            {
                                if (nextLog.IsIgnoreLogTextFile)
                                    continue;
                                if (nextLog is CallMethodLogInformation)
                                    WriteToFile((CallMethodLogInformation)nextLog);
                                else if (nextLog is HttpCallMethodLogInformation)
                                    WriteToFile((HttpCallMethodLogInformation)nextLog);
                                else if (nextLog is CallClientMethodLogInformation)
                                    WriteToFile((CallClientMethodLogInformation)nextLog);
                            }
                            else
                            {
                                AutoLogger.LogText("WTF MethodCallsLogger StartEngine");
                            }
                        }
                        else
                        {
                            Thread.Sleep(1000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AutoLogger.LogError(ex, "MethodCallsLogger StartEngine");
                }
                waitForDispose.Set();
                isStop = true;
            });
            _thread.IsBackground = true;
            _thread.Start();
#endif
        }

        static ConcurrentQueue<BaseLogInformation> Logs = new ConcurrentQueue<BaseLogInformation>();

        public static CallMethodLogInformation AddCallMethodLog(string clientId, string ipAddress, DateTime connectedDateTime, string serviceName, MethodInfo method, List<Models.ParameterInfo> parameters)
        {
            if (isStop)
                return null;
            var log = new CallMethodLogInformation() { DateTimeStartMethod = DateTime.Now.ToLocalTime(), Method = method, Parameters = parameters, ServiceName = serviceName, ConnectedDateTime = connectedDateTime, IPAddress = ipAddress, ClientId = clientId, MethodName = method?.Name };
            Logs.Enqueue(log);
            return log;
        }

        public static HttpCallMethodLogInformation AddHttpMethodLog(string clientId, string ipAddress, DateTime connectedDateTime, string address, MethodInfo method, List<string> parameters)
        {
            if (isStop)
                return null;
            var log = new HttpCallMethodLogInformation() { DateTimeStartMethod = DateTime.Now.ToLocalTime(), Method = method, Parameters = parameters, Address = address, ConnectedDateTime = connectedDateTime, IPAddress = ipAddress, ClientId = clientId, MethodName = method?.Name };
            Logs.Enqueue(log);
            return log;
        }

        public static CallClientMethodLogInformation AddCallClientMethodLog(string clientId, string ipAddress, DateTime connectedDateTime, string serviceName, string methodName, List<Models.ParameterInfo> parameters)
        {
            if (isStop)
                return null;
            var log = new CallClientMethodLogInformation() { DateTimeStartMethod = DateTime.Now.ToLocalTime(), MethodName = methodName, Parameters = parameters, ServiceName = serviceName, ConnectedDateTime = connectedDateTime, IPAddress = ipAddress, ClientId = clientId };
            Logs.Enqueue(log);
            return log;
        }

        public static void FinishLog(CallMethodLogInformation log, object result)
        {
            if (isStop)
                return;
            if (log == null)
                return;
            log.DateTimeEndMethod = DateTime.Now.ToLocalTime();
            if (log.Exception == null)
                log.Result = result;
            OnServiceMethodCalledAction?.Invoke(log);
            log.CanWriteToFile = true;
        }

        public static void FinishLog(HttpCallMethodLogInformation log, object result)
        {
            if (isStop)
                return;
            if (log == null)
                return;
            log.DateTimeEndMethod = DateTime.Now.ToLocalTime();
            if (log.Exception == null)
                log.Result = result;
            OnHttpServiceMethodCalledAction?.Invoke(log);
            log.CanWriteToFile = true;
        }

        public static void FinishLog(CallClientMethodLogInformation log, object result)
        {
            if (isStop)
                return;
            if (log == null)
                return;
            log.DateTimeEndMethod = DateTime.Now.ToLocalTime();
            if (log.Exception == null)
                log.Result = result;
            OnServiceCallbackMethodCalledAction?.Invoke(log);
            log.CanWriteToFile = true;
        }

        static string CombinePath(params string[] pathes)
        {
            string result = pathes[0];
            foreach (var item in pathes.Skip(1))
            {
                result = System.IO.Path.Combine(result, item);
            }
            return result;
        }

        static void WriteToFile(CallMethodLogInformation log)
        {
#if (PORTABLE)
            throw new NotSupportedException();
#else
#if (NET35)
            string path = CombinePath(AutoLogger.ApplicationDirectory, "Logs", log.DateTimeStartMethod.Year.ToString(), log.DateTimeStartMethod.Month.ToString(), log.DateTimeStartMethod.Day.ToString());
#else
            string path = System.IO.Path.Combine(AutoLogger.ApplicationDirectory, "Logs", log.DateTimeStartMethod.Year.ToString(), log.DateTimeStartMethod.Month.ToString(), log.DateTimeStartMethod.Day.ToString());
#endif
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);
            path = System.IO.Path.Combine(path, $"{log.DateTimeStartMethod.Year}-{log.DateTimeStartMethod.Month}-{log.DateTimeStartMethod.Day} {log.DateTimeStartMethod.ToLocalTime().Hour}.log");

            StringBuilder build = new StringBuilder();
            build.AppendLine("########################################");
            build.AppendLine("Client Information:");
            build.AppendLine($"	Ip Address:	{log.IPAddress}");
            build.AppendLine($"	ClientId:	{log.ClientId}");
            build.AppendLine($"	Connected Time:	{GetDateTimeString(log.ConnectedDateTime)}");
            build.AppendLine("");
            build.AppendLine($"Call Information:");
            build.AppendLine($"	Service Name:	{log.ServiceName}");
            build.Append($"	Method:		{log.Method.Name}(");
            bool isFirst = true;
            foreach (var parameter in log.Method.GetParameters())
            {
                build.Append((isFirst ? "" : ",") + parameter.ParameterType.Name + " " + parameter.Name);
                isFirst = false;
            }
            build.AppendLine(")");

            build.AppendLine($"	With Values:");
            foreach (var parameter in log.Parameters)
            {
                build.AppendLine("			" + (parameter.Value == null ? "Null" : JsonConvert.SerializeObject(parameter.Value, Formatting.None, new JsonSerializerSettings() { Formatting = Formatting.None }).Replace(@"\""", "")));
            }
            build.AppendLine("");

            if (log.Exception == null)
            {
                build.AppendLine($"Result Information:");
                build.AppendLine("			" + (log.Result == null ? "Null" : JsonConvert.SerializeObject(log.Result, Formatting.None, new JsonSerializerSettings() { Formatting = Formatting.None })).Replace(@"\""", ""));
                build.AppendLine("");
            }
            else
            {
                build.AppendLine($"Exception:");
                build.AppendLine("			" + log.Exception.ToString());
                build.AppendLine("");
            }


            build.AppendLine($"Invoked Time:");
            build.AppendLine($"			{GetDateTimeString(log.DateTimeStartMethod)}");
            build.AppendLine($"Result Time:");
            build.AppendLine($"			{GetDateTimeString(log.DateTimeEndMethod)}");
            build.AppendLine("----------------------------------------------------------------------------------------");
            build.AppendLine("");
            using (var stream = new System.IO.FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                stream.Seek(0, System.IO.SeekOrigin.End);
                byte[] bytes = Encoding.UTF8.GetBytes(build.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
#endif
        }

        static void WriteToFile(CallClientMethodLogInformation log)
        {
#if (PORTABLE)
            throw new NotSupportedException();
#else
#if (NET35)
            string path = CombinePath(AutoLogger.ApplicationDirectory, "Logs", log.DateTimeStartMethod.Year.ToString(), log.DateTimeStartMethod.Month.ToString(), log.DateTimeStartMethod.Day.ToString());
#else
            string path = System.IO.Path.Combine(AutoLogger.ApplicationDirectory, "Logs", log.DateTimeStartMethod.Year.ToString(), log.DateTimeStartMethod.Month.ToString(), log.DateTimeStartMethod.Day.ToString());
#endif
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);
            path = System.IO.Path.Combine(path, $"Callback-{log.DateTimeStartMethod.Year}-{log.DateTimeStartMethod.Month}-{log.DateTimeStartMethod.Day} {log.DateTimeStartMethod.ToLocalTime().Hour}.log");

            StringBuilder build = new StringBuilder();
            build.AppendLine("########################################");
            build.AppendLine("Client Information:");
            build.AppendLine($"	Ip Address:	{log.IPAddress}");
            build.AppendLine($"	ClientId:	{log.ClientId}");
            build.AppendLine($"	Connected Time:	{GetDateTimeString(log.ConnectedDateTime)}");
            build.AppendLine("");
            build.AppendLine($"Call Information:");
            build.AppendLine($"	Service Name:	{log.ServiceName}");
            build.Append($"	Method:		{log.MethodName}(");
            bool isFirst = true;
            int index = 1;
            foreach (var parameter in log.Parameters)
            {
                build.Append((isFirst ? "" : ",") + (parameter.Type == null ? "Null" : parameter.Type) + " obj" + index);
                isFirst = false;
                index++;
            }
            build.AppendLine(")");

            build.AppendLine($"	With Values:");
            foreach (var parameter in log.Parameters)
            {
                build.AppendLine("			" + (parameter.Value == null ? "Null" : JsonConvert.SerializeObject(parameter.Value, Formatting.None, new JsonSerializerSettings() { Formatting = Formatting.None }).Replace(@"\""", "")));
            }
            build.AppendLine("");
            if (log.Exception == null)
            {
                build.AppendLine($"Result Information:");
                build.AppendLine("			" + (log.Result == null ? "Null" : JsonConvert.SerializeObject(log.Result, Formatting.None, new JsonSerializerSettings() { Formatting = Formatting.None })).Replace(@"\""", ""));
                build.AppendLine("");
            }
            else
            {
                build.AppendLine($"Exception:");
                build.AppendLine("			" + log.Exception.ToString());
                build.AppendLine("");
            }
            build.AppendLine($"Invoked Time:");
            build.AppendLine($"			{GetDateTimeString(log.DateTimeStartMethod)}");
            build.AppendLine($"Result Time:");
            build.AppendLine($"			{GetDateTimeString(log.DateTimeEndMethod)}");
            build.AppendLine("----------------------------------------------------------------------------------------");
            build.AppendLine("");
            using (var stream = new System.IO.FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                stream.Seek(0, System.IO.SeekOrigin.End);
                byte[] bytes = Encoding.UTF8.GetBytes(build.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
#endif
        }

        static void WriteToFile(HttpCallMethodLogInformation log)
        {
#if (PORTABLE)
            throw new NotSupportedException();
#else
#if (NET35)
            string path = CombinePath(AutoLogger.ApplicationDirectory, "Logs", log.DateTimeStartMethod.Year.ToString(), log.DateTimeStartMethod.Month.ToString(), log.DateTimeStartMethod.Day.ToString());
#else
            string path = System.IO.Path.Combine(AutoLogger.ApplicationDirectory, "Logs", log.DateTimeStartMethod.Year.ToString(), log.DateTimeStartMethod.Month.ToString(), log.DateTimeStartMethod.Day.ToString());
#endif
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);
            path = System.IO.Path.Combine(path, $"HTTP-{log.DateTimeStartMethod.Year}-{log.DateTimeStartMethod.Month}-{log.DateTimeStartMethod.Day} {log.DateTimeStartMethod.ToLocalTime().Hour}.log");

            StringBuilder build = new StringBuilder();
            build.AppendLine("########################################");
            build.AppendLine("Client Information:");
            build.AppendLine($"	Ip Address:	{log.IPAddress}");
            build.AppendLine($"	ClientId:	{log.ClientId}");
            build.AppendLine($"	Connected Time:	{GetDateTimeString(log.ConnectedDateTime)}");
            build.AppendLine("");
            build.AppendLine($"Call Information:");
            build.AppendLine($"	Address:	{log.Address}");
            build.Append($"	Method:		{log.Method.Name}(");
            bool isFirst = true;
            foreach (var parameter in log.Method.GetParameters())
            {
                build.Append((isFirst ? "" : ",") + parameter.ParameterType.Name + " " + parameter.Name);
                isFirst = false;
            }
            build.AppendLine(")");

            build.AppendLine($"	With Values:");
            foreach (var value in log.Parameters)
            {
                build.AppendLine("			" + (value == null ? "Null" : value));
            }
            build.AppendLine("");
            if (log.Exception == null)
            {
                build.AppendLine($"Result Information:");
                build.AppendLine("			" + (log.Result == null ? "Null" : JsonConvert.SerializeObject(log.Result, Formatting.None, new JsonSerializerSettings() { Formatting = Formatting.None })).Replace(@"\""", ""));
                build.AppendLine("");
                build.AppendLine("			" + (log.Result == null ? "Null" : log.Result.GetType().FullName));
                build.AppendLine("");
            }
            else
            {
                build.AppendLine($"Exception:");
                build.AppendLine("			" + log.Exception.ToString());
            }
            build.AppendLine($"Invoked Time:");
            build.AppendLine($"			{GetDateTimeString(log.DateTimeStartMethod)}");
            build.AppendLine($"Result Time:");
            build.AppendLine($"			{GetDateTimeString(log.DateTimeEndMethod)}");
            build.AppendLine("----------------------------------------------------------------------------------------");
            build.AppendLine("");
            using (var stream = new System.IO.FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                stream.Seek(0, System.IO.SeekOrigin.End);
                byte[] bytes = Encoding.UTF8.GetBytes(build.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
#endif
        }

        static string GetDateTimeString(DateTime dt)
        {
#if (PORTABLE)
            throw new NotSupportedException();
#else
            PersianCalendar persian = new PersianCalendar();
            string log = "";
            if (IsPersianDateLog)
                log = $"{persian.GetYear(dt)}/{persian.GetMonth(dt)}/{persian.GetDayOfMonth(dt)} {persian.GetHour(dt)}:{persian.GetMinute(dt)}:{persian.GetSecond(dt)}.{persian.GetMilliseconds(dt)} ## ";
            return log + $"{dt.ToString("MM/dd/yyyy HH:mm:ss")}.{dt.Millisecond}";
#endif
        }
    }
}
