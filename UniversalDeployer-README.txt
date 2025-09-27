UniversalDeployer (NET Framework 4.8)
=====================================

A single console tool to INSTALL / UPDATE / UNINSTALL an application by copying files
from a staging folder to a final target directory, and (optionally) installing/updating/
removing one or more Windows Services — all driven by a JSON config file.

-------------------------------------------------------------------------------
1) Requirements
-------------------------------------------------------------------------------
- Windows with .NET Framework 4.8
- Administrator rights (Run CMD/PowerShell as Administrator)
- Your app files (EXE/DLLs/configs) staged in a folder (default: .\payload)

-------------------------------------------------------------------------------
2) Quick Start
-------------------------------------------------------------------------------
1. Put your application build output into the 'payload' folder (or change App.SourceDir).
2. Open appsettings.json and tailor the config (TargetDir, Services, etc.).
3. Open an elevated terminal in the folder containing UniversalDeployer.exe.
4. Run one of:

   UniversalDeployer.exe install   --config ".\appsettings.json"
   UniversalDeployer.exe update    --config ".\appsettings.json"
   UniversalDeployer.exe uninstall --config ".\appsettings.json"

- install:   Copies files to TargetDir, creates/starts services if missing.
- update:    Stops services, copies files (clean + preserve rules), restarts services.
- uninstall: Stops & deletes services, cleans files (respects preserve + extra deletes).

-------------------------------------------------------------------------------
3) Configuration (appsettings.json)
-------------------------------------------------------------------------------
{
  "App": {
    "Id": "YourAppId",
    "SourceDir": ".\\payload",
    "TargetDir": "C:\\Services\\YourApp",
    "CleanTarget": true,
    "Preserve": [ "logs\\*", "config\\*.json" ],
    "StopProcesses": [ "YourApp.exe" ],
    "PostInstallRun": [ ],
    "PostUninstallDelete": [ "logs\\*" ]
  },
  "Services": [
    {
      "Name": "your-service-name",
      "DisplayName": "Your Service",
      "Description": "What the service does",
      "Exe": "YourApp.exe",
      "StartType": "auto",           // auto | demand | disabled
      "Account": "LocalSystem",      // or .\User / DOMAIN\User
      "Password": null,
      "FailureActions": "reset= 86400 actions= restart/60000/restart/60000/restart/60000"
    }
  ]
}

Fields explained
----------------
App.*
- Id:                     Free text label for the deployment (optional).
- SourceDir:              Folder containing the files to deploy (default: .\payload).
- TargetDir:              Final installation directory (will be created).
- CleanTarget:            If true, files in TargetDir that are NOT in SourceDir are removed
                          (except those matching Preserve globs).
- Preserve:               Glob patterns (relative to TargetDir) to keep during update/uninstall
                          e.g., "logs\*", "config\*.json". Supports *, ?, **.
- StopProcesses:          Process names (or EXEs) to terminate before copying (avoid file locks).
- PostInstallRun:         Commands to run after install/update (e.g., "sc start your-service").
- PostUninstallDelete:    Extra globs to delete during uninstall (in addition to not preserved).

Services[] (optional)
- Name:            Windows ServiceName (NOT Display Name). Get via: Get-Service | Select Name,DisplayName
- DisplayName:     Friendly name in Services.msc.
- Description:     Service description (optional).
- Exe:             Service executable path relative to TargetDir (e.g., "YourApp.exe").
- StartType:       auto | demand | disabled
- Account:         "LocalSystem" or an account (".\User" or "DOMAIN\User").
- Password:        Password for the account (if not LocalSystem).
- FailureActions:  Passed to 'sc.exe failure' (optional). Example restarts 3x with 60s delay.

-------------------------------------------------------------------------------
4) How it works
-------------------------------------------------------------------------------
- Stops any configured services and listed processes (StopProcesses) to avoid locked files.
- Copies files from SourceDir to TargetDir. If CleanTarget=true, prunes extra files not in
  SourceDir (except Preserve globs).
- Creates/updates services using sc.exe:
    - 'sc create' if they don't exist, otherwise 'sc config' (binary path, start type).
    - Sets description / failure actions if configured.
    - Starts them after copy (install/update). Stops & deletes them on uninstall.
- Leaves files matched by Preserve globs intact during update/uninstall (useful for logs/config).

-------------------------------------------------------------------------------
5) Examples
-------------------------------------------------------------------------------
Update with custom config:
  UniversalDeployer.exe update --config "C:\deploy\myapp.json"

Install to Program Files (quote paths with spaces):
  UniversalDeployer.exe install --config ".\appsettings.json"

Uninstall completely but keep logs:
  (Ensure 'logs\*' is in App.Preserve) then run:
  UniversalDeployer.exe uninstall --config ".\appsettings.json"

-------------------------------------------------------------------------------
6) Tips & Best Practices
-------------------------------------------------------------------------------
- Always run as Administrator.
- Use 'Preserve' to keep environment-specific files and logs.
- If using a custom service account, ensure the account has "Log on as a service" rights.
- For multi-service apps, add multiple entries under Services[].
- To move payload elsewhere, adjust App.SourceDir (absolute or relative).

-------------------------------------------------------------------------------
7) Troubleshooting
-------------------------------------------------------------------------------
- ERROR: Access denied / service control failed
  -> Run the terminal as Administrator. Ensure account has privileges on the machine.

- ERROR: File in use / cannot overwrite
  -> Add the EXE/process name to App.StopProcesses and ensure services are stopped.

- The service won't start
  -> Check Windows Event Log (Application/System) and your app logs in TargetDir.
     Make sure 'Exe' in config points to the correct file under TargetDir.

- Unknown action
  -> Use: install | update | uninstall

-------------------------------------------------------------------------------
8) Safety & Rollback (optional enhancement idea)
-------------------------------------------------------------------------------
For production, consider adding a pre-update backup of TargetDir (zip) and a 'rollback'
command that restores the previous version if the start fails.
