using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using SebScheduler.Core;
using SebScheduler.SqlBackup;

namespace SebScheduler.BackupEngine
{
    public class BackupEnginService : ServiceBase
    {
        private Timer _timer;
        private List<BackupJob> _backupJobs = new List<BackupJob>();
        private FileSystemWatcher _backupJobsFileSystemWatcher;

        public BackupEnginService()
        {
            ServiceName = GlobalSettings.ApplicationName;
        }

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "/i":
                        ManagedInstallerClass.InstallHelper(new[] { Assembly.GetExecutingAssembly().Location });
                        break;
                    case "/u":
                        try
                        {
                            ManagedInstallerClass.InstallHelper(new[] { "/u", Assembly.GetExecutingAssembly().Location });
                        }
                        catch
                        {
                        }
                        break;
                    case "/d":
                        {
                            var service = new BackupEnginService();
                            service.OnStart(null);
                            Thread.Sleep(Timeout.Infinite);
                        }
                        break;
                }
            }
            else
            {
                var servicesToRun = new ServiceBase[] { new BackupEnginService() };
                Run(servicesToRun);
            }
        }
        private void TimerElapsed(object state)
        {
            foreach (var backupJob in _backupJobs)
            {
                if (!backupJob.IsTimeToRun()) continue;
                ISqlBackup sqlBackup = new SqlExpress();
                sqlBackup.CreateBackup(backupJob);
                return;
            }
        }

        protected override void OnStart(string[] args)
        {
            _timer = new Timer(TimerElapsed, null, 0, 60000);
            InitLoadBackupJobs();
        }

        private void InitLoadBackupJobs()
        {
            LoadBackupJobs();
            _backupJobsFileSystemWatcher = new FileSystemWatcher();
            if (_backupJobsFileSystemWatcher != null) return;
            _backupJobsFileSystemWatcher = new FileSystemWatcher(GlobalSettings.AppDataSettingsPath);
            _backupJobsFileSystemWatcher.BeginInit();
            _backupJobsFileSystemWatcher.Created += FileSystemWatcherEvent;
            _backupJobsFileSystemWatcher.Renamed += FileSystemWatcherRenamed;
            _backupJobsFileSystemWatcher.Deleted += FileSystemWatcherEvent;
            _backupJobsFileSystemWatcher.Changed += FileSystemWatcherEvent;
            _backupJobsFileSystemWatcher.IncludeSubdirectories = true;
            _backupJobsFileSystemWatcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName;
            _backupJobsFileSystemWatcher.EnableRaisingEvents = true;
            _backupJobsFileSystemWatcher.EndInit();
        }

        private void FileSystemWatcherEvent(object sender, FileSystemEventArgs e)
        {
            if (e.Name.ToUpperInvariant() != GlobalSettings.BackupJobsConfigFile.ToUpperInvariant()) return;
            Thread.Sleep(1000);
            LoadBackupJobs();
        }

        private void FileSystemWatcherRenamed(object sender, RenamedEventArgs e)
        {
            var fileName = GlobalSettings.BackupJobsConfigFile.ToUpperInvariant();
            if ((e.OldName.ToUpperInvariant() != fileName && e.Name.ToUpperInvariant() != fileName)) return;
            Thread.Sleep(1000);
            LoadBackupJobs();
        }

        private void LoadBackupJobs()
        {
            _backupJobs.Clear();
            if (!File.Exists(GlobalSettings.BackupJobsConfigFile)) return;
            _backupJobs = GlobalSettings.LoadSettingsFile<List<BackupJob>>(GlobalSettings.BackupJobsConfigFile);
        }

        protected override void OnStop()
        {
            _timer.Dispose();
            _timer = null;
        }
    }
}
