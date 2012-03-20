using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;
using SebScheduler.Core;


namespace SebScheduler.BackupEngine
{
    [RunInstaller(true)]
    public class ProjectInstaller : Installer
    {
        private readonly ServiceProcessInstaller _process;
        private readonly ServiceInstaller _service;

        public ProjectInstaller()
        {
            _process = new ServiceProcessInstaller {Account = ServiceAccount.LocalSystem};
            _service = new ServiceInstaller { ServiceName = GlobalSettings.ApplicationName };
            Installers.Add(_process);
            Installers.Add(_service);
        }
    }
}
