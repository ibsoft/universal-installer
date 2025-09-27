# UniversalDeployer (.NET Framework 4.8)

A single console tool to **install / update / uninstall** an application by copying files from a staging folder to a final target directory, and optionally **create/update/remove Windows Services**, all driven by a **JSON config**.

## Usage
From an elevated prompt (Run as Administrator):
```cmd
UniversalDeployer.exe install   --config ".\appsettings.json"
UniversalDeployer.exe update    --config ".\appsettings.json"
UniversalDeployer.exe uninstall --config ".\appsettings.json"
```

### Config (appsettings.json)
- `App.SourceDir`: folder with your build output (all files to deploy).
- `App.TargetDir`: final directory (created if missing).
- `App.CleanTarget`: if true, removes files in Target that are not in `SourceDir` (except the `Preserve` globs).
- `App.Preserve`: glob patterns to keep (e.g., logs, local config files).
- `App.StopProcesses`: process names to kill before file ops.
- `App.PostInstallRun`: commands to run after install/update.
- `App.PostUninstallDelete`: extra globs to delete on uninstall.
- `Services`: list of Windows Services to manage (optional).

> Tip: Put your app files under `payload\`. Edit `appsettings.json` and run installer.

### Service fields
- `Name`: ServiceName (not display name).
- `DisplayName`, `Description`: optional cosmetics.
- `Exe`: relative path from `TargetDir` to the service executable.
- `StartType`: `auto` / `demand` / `disabled`.
- `Account`: `LocalSystem` or account (`.\User`, `DOMAIN\User`). If custom account, set `Password`.
- `FailureActions`: passed to `sc.exe failure` (optional).

### What it does
- **install**: create TargetDir, copy files, create service(s) if missing, configure and start them.
- **update**: stop services, copy files (with preserve & clean), restart services.
- **uninstall**: stop and delete services, delete files (respect preserve + PostUninstallDelete).

> Requires Administrator for service control and writing to Program Files / Services folders.
