﻿using MvvmGo.ViewModels;
using SignalGo.Publisher.Engines.Interfaces;
using SignalGo.Publisher.Engines.Models;
using SignalGo.Publisher.Extensions;
using SignalGo.Publisher.Models;
using SignalGo.Publisher.Models.DataTransferObjects;
using SignalGo.Publisher.Models.Extra;
using SignalGo.Publisher.Services;
using SignalGo.Publisher.Shared.Helpers;
using SignalGo.Publisher.Shared.Models;
using SignalGo.Shared.DataTypes;
using SignalGo.Shared.Log;
using SignalGo.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SignalGo.Publisher.Engines.Commands
{
    public abstract class CommandBaseInfo : BaseViewModel, ICommand, IPublish
    {
        private RunStatusType _Status;
        public RunStatusType Status
        {
            get => _Status; set
            {
                _Status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(HasStatusError));
            }
        }
        public bool IsComplete
        {
            get
            {
                return Status == RunStatusType.Error || Status == RunStatusType.Done;
            }
        }
        public bool HasStatusError
        {
            get
            {
                return Status == RunStatusType.Error;
            }
        }

        private long _Size = 0;
        private long _Position = 0;

        /// <summary>
        /// size of data/file
        /// </summary>
        public long Size
        {
            get
            {
                return _Size;// / 1024;
            }
            set
            {
                _Size = value;
                OnPropertyChanged("Size");
            }
        }

        /// <summary>
        /// current position of stream/file
        /// </summary>
        public long Position
        {
            get
            {
                return _Position;// / 1024;
            }
            set
            {
                _Position = value;
                OnPropertyChanged("Position");
            }
        }
        public string Name { get; set; }
        public string ExecutableFile { get; set; }
        public string Command { get; set; }
        public string Arguments { get; set; }
        public bool IsEnabled { get; set; }
        /// <summary>
        /// working/project directory path that process start on it
        /// </summary>
        public string WorkingPath { get; set; }
        /// <summary>
        /// assemblies and publish foler/files path
        /// </summary>
        public string AssembliesPath { get; set; }
        public string ServiceName { get; set; }
        public Guid ServiceKey { get; set; }

        /// <summary>
        /// Base Run Module for commands
        /// </summary>
        /// <returns></returns>
        public virtual async Task<RunStatusType> Run(CancellationToken cancellationToken, string caller)
        {
            try
            {
                Status = RunStatusType.Running;
                if (cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine("Cancellation Requested in Runner Module");
                    Status = RunStatusType.Canceled;
                    return RunStatusType.Canceled;
                }

                var processStatus = await CommandRunner.Run(this, cancellationToken);
                if (processStatus == RunStatusType.Error || processStatus == RunStatusType.Canceled)
                    Status = RunStatusType.Error;

                return processStatus;
            }
            catch (Exception ex)
            {
                AutoLogger.Default.LogError(ex, "Runner Module, CommandBaseInfo");
                Status = RunStatusType.Error;
                return RunStatusType.Canceled;
            }
            finally
            {
                // read the last command log's
                await ReadCommandLog(caller);
            }
        }

        /// <summary>
        /// Read the excecuted command logs (verbosity based on IsFullLogging in user settings) default read last 5 line of logs
        /// </summary>
        /// <param name="caller">who asked command logs (for display in ui sector's)</param>
        /// <param name="recent">only read the recent logs (reverse)</param>
        /// <param name="count">Get specific number of lines from the logs</param>
        /// <returns></returns>
        public async Task ReadCommandLog(string caller, bool recent = false, int count = 5)
        {
            IEnumerable<string> logs;
            // if the user had defined log verbosity to full:
            if (UserSettingInfo.Current.UserSettings.LoggingVerbosity == UserSetting.LoggingVerbosityEnum.Minimuum)
            {
                // take latest logs based on the count is defined 
                logs = File.ReadLines(UserSettingInfo.Current.UserSettings.CommandRunnerLogsPath).TakeLast(count);
            }
            // log verbosity minimuum:
            else if (UserSettingInfo.Current.UserSettings.LoggingVerbosity == UserSetting.LoggingVerbosityEnum.Full && !recent)
            {
                // take all logs
                logs = await File.ReadAllLinesAsync(UserSettingInfo.Current.UserSettings.CommandRunnerLogsPath);
            }
            else
            {
                logs = Enumerable.Empty<string>();
            }
            foreach (string item in logs)
            {
                LogModule.AddLog(caller, SectorType.Builder, item, DateTime.Now.ToLongTimeString(), LogTypeEnum.Compiler);
            }
            for (int i = 0; i < ServerInfo.ServerLogs.Count; i++)
            {
                LogModule.AddLog(caller, SectorType.Server, ServerInfo.ServerLogs[i], DateTime.Now.ToLongTimeString(), LogTypeEnum.Compiler);
            }
        }
        public virtual async Task<List<CompressArchiveDto>> Compress(CompressionMethodType compressionMethod = CompressionMethodType.Zip, bool includeParent = false, CompressionLevel compressionLevel = CompressionLevel.Fastest, bool compressOnlyChanges = true)
        {
            var zipFilePath = string.Empty;
            var result = new List<CompressArchiveDto>();

            try
            {
                string[] directories = Directory.GetDirectories(AssembliesPath);
                string publishDir = directories.FirstOrDefault(x => x.Contains("publish"));
                if (string.IsNullOrEmpty(publishDir))
                {
                    publishDir = Path.Combine(AssembliesPath, "publish");
                }
                Directory.CreateDirectory(publishDir);

                //
                var originHashes = FileHelper.CalculateFileHashesInDirectory(publishDir);
                var selectedServers = ServerSettingInfo.CurrentServer.ServerInfo.Where(x => x.IsChecked).ToList();

                foreach (var server in selectedServers)
                {
                    zipFilePath = Path.Combine(publishDir, $"{ServiceName}_{server.ServerKey}.zip");
                    if (File.Exists(zipFilePath))
                        File.Delete(zipFilePath);

                    originHashes.RemoveAll(x => x.FilePath.Equals(zipFilePath));

                    var provider = await PublisherServiceProvider.Initialize(server, ServiceName);
                    //if (!provider.HasValue())
                    //    return RunStatusType.Error;

                    var service = provider.FileManagerService;
                    var destinationHashes = service.CalculateFileHashes(ServiceKey);
                    var comparedHashes = CompareFileHashes(originHashes, destinationHashes);
                    SettingInfo.Current.ProjectInfo.FirstOrDefault(p => p.ProjectKey == ServiceKey).ServerIgnoredFiles.ToList()
                        .ForEach(x =>
                        {
                            comparedHashes.FirstOrDefault(y => y.FileName.Equals(x.FileName))?.MarkAsIgnored();
                        });

                    switch (compressionMethod)
                    {
                        case CompressionMethodType.None:
                            break;
                        case CompressionMethodType.Zip:
                            if (compressOnlyChanges)
                            {
                                var excludedStates = new FileStatusType[] { FileStatusType.Ignored, FileStatusType.Deleted, FileStatusType.Unchanged };
                                ZipHelper.CreateFromFileList(comparedHashes, zipFilePath, CompressionLevel.Optimal, excludedStates);
                            }
                            else
                                ZipFile.CreateFromDirectory(publishDir, zipFilePath, CompressionLevel.Optimal, includeParent);

                            result.Add(new CompressArchiveDto() { ArchivePath = zipFilePath, FileHashes = comparedHashes });
                            break;
                        case CompressionMethodType.Gzip:
                            throw new NotImplementedException("Gzip method not implemented yet.");
                            break;
                        case CompressionMethodType.Rar:
                            throw new NotImplementedException("Rar method not implemented yet.");
                            break;
                        case CompressionMethodType.Tar:
                            throw new NotImplementedException("Tar method not implemented yet.");
                            break;
                        case CompressionMethodType.bzip:
                            throw new NotImplementedException("bzip method not implemented yet.");
                            break;
                        case CompressionMethodType.Zip7:
                            throw new NotImplementedException("7Zip method not implemented yet.");
                            break;
                        default:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                AutoLogger.Default.LogError(ex, "Publisher Compression");
            }
            //return Task.FromResult(zipFilePath);
            return result;
        }
        public virtual async Task<RunStatusType> Upload(List<CompressArchiveDto> compressedData, CancellationToken cancellationToken, bool forceUpdate = false)
        {
            var status = RunStatusType.Error;
            if (cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine("Cancellation Requested In Upload Command");
                return RunStatusType.Canceled;
            }
            try
            {
                ServerInfo.ServerLogs.Add($"----(Manually)Started at [{DateTime.Now}] ------");
                LogModule.AddLog(ServiceName, SectorType.Server, $"----(Manually)Started at [{DateTime.Now}] ------", LogTypeEnum.System);

                // set recieved service contract to out contract(to customize if needed)
                var serviceContract = new Shared.Models.ServiceContract
                {
                    Name = ServiceName,
                    ServiceKey = ServiceKey,
                    IgnoreFiles = SettingInfo.Current.ProjectInfo.FirstOrDefault(p => p.ProjectKey == ServiceKey).ServerIgnoredFiles.Where(e => e.IsEnabled).Select(x => new IgnoreFileDto()
                    {
                        FileName = x.FileName,
                        ID = x.ID,
                        IgnoreFileType = x.IgnoreFileType,
                        IsEnabled = x.IsEnabled,
                        ProjectId = x.ProjectId
                    }).ToList()
                };
                List<ServerInfo> selectedServers = ServerSettingInfo.CurrentServer.ServerInfo.Where(x => x.IsChecked).ToList();

                //List<ServerInfo> serversToUpdate = selectedServers.Where(x => x.IsUpdated != ServerInfo.ServerInfoStatusEnum.UpdateError).ToList().Where(y => y.IsUpdated != ServerInfo.ServerInfoStatusEnum.Updated).ToList();

                for (int i = 0; i < selectedServers.Count; i++)
                {
                    ServerInfo server = selectedServers[i];
                    var currentSrv = ServerSettingInfo.CurrentServer.ServerInfo.FirstOrDefault(x => x.ServerKey == server.ServerKey);
                    currentSrv.ServerStatus = ServerInfo.ServerInfoStatusEnum.Updating;
                    var provider = await PublisherServiceProvider.Initialize(currentSrv, serviceContract.Name);
                    if (!provider.HasValue())
                        return RunStatusType.Error;

                    // Generate Upload Model based on
                    var currentCompressed = compressedData.FirstOrDefault(x => x.ArchivePath.Contains(currentSrv.ServerKey.ToString()));
                    if (currentCompressed == null)
                        return RunStatusType.Error;

                    Size = (new FileInfo(currentCompressed.ArchivePath).Length / 1024);
                    UploadInfo uploadInfo = new UploadInfo(this)
                    {
                        FileName = "publishArchive.zip",
                        HasProgress = true,
                        FilePath = currentCompressed.ArchivePath
                    };
                    serviceContract.CompressArchive = currentCompressed;
                    var uploadResult = await StreamManagerService.UploadAsync(uploadInfo, cancellationToken, serviceContract, provider.CurrentClientProvider);

                    if (uploadResult.Status)
                    {
                        ServerInfo.ServerLogs.Add($"------ Ended at [{DateTime.Now}] ------");
                        LogModule.AddLog(ServiceName, SectorType.Server, $"------ Ended at [{DateTime.Now}] ------", LogTypeEnum.System);
                        server.IsUpdated = ServerInfo.ServerInfoStatusEnum.Updated;
                        server.ServerStatus = ServerInfo.ServerInfoStatusEnum.Updated;

                        currentSrv.ServerStatus = ServerInfo.ServerInfoStatusEnum.Updated;
                        currentSrv.IsUpdated = ServerInfo.ServerInfoStatusEnum.Updated;
                        currentSrv.ServerLastUpdate = DateTime.Now.ToString();
                        // save project last update status
                        SettingInfo.Current.ProjectInfo.SingleOrDefault(k => k.ProjectKey == ServiceKey).LastUpdateDateTime = DateTime.Now.ToString();

                        SettingInfo.SaveSettingInfo();
                        ServerSettingInfo.SaveServersSettingInfo();
                        status = RunStatusType.Done;

                    } // end server collection for
                    else
                    {
                        server.IsUpdated = ServerInfo.ServerInfoStatusEnum.UpdateError;
                        server.ServerStatus = ServerInfo.ServerInfoStatusEnum.UpdateError;
                        selectedServers.FirstOrDefault(s => s.ServerKey == server.ServerKey).ServerStatus = ServerInfo.ServerInfoStatusEnum.UpdateError;
                        selectedServers.FirstOrDefault(s => s.ServerKey == server.ServerKey).IsUpdated = ServerInfo.ServerInfoStatusEnum.UpdateError;
                        currentSrv.ServerStatus = ServerInfo.ServerInfoStatusEnum.UpdateError;
                        currentSrv.IsUpdated = ServerInfo.ServerInfoStatusEnum.UpdateError;
                        currentSrv.ServerLastUpdate = "Couldn't Update";
                    }
                }
            }
            catch (Exception ex)
            {
                // may be service is in use and started before. so i need compile it
                ServerInfo.ServerLogs.Add(ex.Message);
            }
            ServerInfo.ServerLogs.Add($"------ Exited at [{DateTime.Now}] ------");
            LogModule.AddLog(ServiceName, SectorType.Server, $"------ Exited at [{DateTime.Now}] ------", LogTypeEnum.System);
            return status;
        }

        public string GetSolutionFileName(string path, string serverDefaultSolutionShortName)
        {
            var solutionFiles = Directory.GetFiles(path, "*.*").Where(x => x.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(serverDefaultSolutionShortName))
                return solutionFiles.FirstOrDefault(x => x.Contains(serverDefaultSolutionShortName));
            else
                return solutionFiles.FirstOrDefault();
        }

        public virtual Task Initialize(ProcessStartInfo processStartInfo)
        {
            return Task.CompletedTask;
        }
        public abstract bool CalculateStatus(string line);

        #region Utilities
        /// <summary>
        /// Sets the status of each file in origin collection based on filename and filehash.
        /// </summary>
        /// <param name="originHashes"></param>
        /// <param name="destinationHashes"></param>
        /// <returns></returns>
        private List<HashedFileDto> CompareFileHashes(List<HashedFileDto> originHashes, List<HashedFileDto> destinationHashes)
        {
            var result = new List<HashedFileDto>();
            var destination = default(HashedFileDto);

            foreach (var origin in originHashes)
            {
                destination = destinationHashes.FirstOrDefault(x => x.FileName.Equals(origin.FileName));
                if (destination == null)
                    origin.MarkAsAdded();
                else
                {
                    if (origin.FileHash.Equals(destination.FileHash))
                        origin.MarkAsUnchanged();
                    else
                        origin.MarkAsModified();
                }

                result.Add(origin);
            }

            //finding files that have to be deleted
            destinationHashes.Where(x => !originHashes.Any(y => x.FileName.Equals(y.FileName))).ToList()
                .ForEach(z =>
                {
                    z.MarkAsDeleted();
                    result.Add(z);
                });

            return result;
        }
        #endregion
    }
}
