using System.Collections.Generic;

namespace UniversalDeployer.Models
{
    public sealed class AppRoot
    {
        public AppConfig App { get; set; } = new AppConfig();
        public List<ServiceConfig> Services { get; set; } = new List<ServiceConfig>();
    }

    public sealed class AppConfig
    {
        public string Id { get; set; }
        public string SourceDir { get; set; }
        public string TargetDir { get; set; }
        public bool CleanTarget { get; set; } = true;
        public List<string> Preserve { get; set; } = new List<string>();
        public List<string> StopProcesses { get; set; } = new List<string>();
        public List<string> PostInstallRun { get; set; } = new List<string>();
        public List<string> PostUninstallDelete { get; set; } = new List<string>();
    }

    public sealed class ServiceConfig
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Exe { get; set; }
        public string StartType { get; set; } = "auto"; // auto|demand|disabled
        public string Account { get; set; } = "LocalSystem";
        public string Password { get; set; }
        public string FailureActions { get; set; }
    }
}
