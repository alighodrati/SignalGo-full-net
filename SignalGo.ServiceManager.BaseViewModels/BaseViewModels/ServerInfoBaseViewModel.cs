﻿using MvvmGo.Commands;
using MvvmGo.ViewModels;
using SignalGo.ServiceManager.Core.Models;
using System;
using System.Linq;

namespace SignalGo.ServiceManager.Core.BaseViewModels
{
    public class ServerInfoBaseViewModel : BaseViewModel
    {
        public ServerInfoBaseViewModel()
        {
            StartCommand = new Command(Start);
            StopCommand = new Command(Stop);
            BrowsePathCommand = new Command(BrowsePath);
            ChangeCommand = new Command(Change);
            DeleteCommand = new Command(Delete);
            ClearLogCommand = new Command(ClearLog);
            CopyCommand = new Command<TextLogInfo>(Copy);
        }

        public Command StartCommand { get; set; }
        public Command StopCommand { get; set; }
        public Command ChangeCommand { get; set; }
        public Command BrowsePathCommand { get; set; }
        public Command DeleteCommand { get; set; }
        public Command ClearLogCommand { get; set; }
        public Command<TextLogInfo> CopyCommand { get; set; }

        ServerInfo _ServerInfo;

        public ServerInfo ServerInfo
        {
            get
            {
                return _ServerInfo;
            }

            set
            {
                _ServerInfo = value;
                OnPropertyChanged(nameof(ServerInfo));
            }
        }

        protected virtual void Delete()
        {
            SettingInfo.Current.ServerInfo.Remove(ServerInfo);
            SettingInfo.SaveSettingInfo();
        }

        private void Stop()
        {
            //ServerInfo.Stop();
            StopServer(ServerInfo);
        }

        private void Start()
        {
            StartServer(ServerInfo);
        }
        private void Change()
        {
            var gu = Guid.Empty;
            if (Guid.TryParse(ServerInfo.ServerKey.ToString(), out gu))
            {
                ServerInfo.ServerKey = gu;
            }
            SettingInfo.SaveSettingInfo();
        }
        protected virtual void BrowsePath()
        {

        }
        public static void Delete(ServerInfo serverInfo)
        {
            StopServer(serverInfo);
            SettingInfo.Current.ServerInfo.Remove(serverInfo);
            SettingInfo.SaveSettingInfo();
        }
        public static void Delete(string serviceName)
        {
            try
            {
                var server = SettingInfo.Current.ServerInfo.FirstOrDefault(x => x.Name == serviceName);
                StopServer(server);
                SettingInfo.Current.ServerInfo.Remove(server);
                SettingInfo.SaveSettingInfo();
            }
            catch (Exception ex)
            {

            }
        }
        public static void StartServer(ServerInfo serverInfo)
        {
            serverInfo.Start();
        }
        public static void StopServer(ServerInfo serverInfo)
        {
            serverInfo.Stop();
        }

        private void ClearLog()
        {
            //ServerInfo.Logs.Clear();
        }

        protected virtual void Copy(TextLogInfo textLogInfo)
        {
        }
    }
}