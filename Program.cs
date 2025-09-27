using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using Newtonsoft.Json;
using UniversalDeployer.Models;
using UniversalDeployer.Util;

namespace UniversalDeployer
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0 || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
                {
                    PrintHelp();
                    return 0;
                }

                var action = args[0].ToLowerInvariant(); // install|update|uninstall
                var configPath = GetArg(args, "--config") ?? "appsettings.json";

                var json = File.ReadAllText(configPath, Encoding.UTF8);
                var root = JsonConvert.DeserializeObject<AppRoot>(json);
                if (root == null) throw new InvalidOperationException("Invalid or empty config: " + configPath);

                EnsureAdmin();

                switch (action)
                {
                    case "install":
                        Install(root);
                        break;
                    case "update":
                        Update(root);
                        break;
                    case "uninstall":
                        Uninstall(root);
                        break;
                    default:
                        Console.Error.WriteLine("Unknown action: " + action);
                        PrintHelp();
                        return 2;
                }

                Console.WriteLine("[OK] " + action + " completed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERROR] " + ex);
                return 1;
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("UniversalDeployer v1");
            Console.WriteLine("Usage:");
            Console.WriteLine("  UniversalDeployer.exe install   --config \"path\\to\\appsettings.json\"");
            Console.WriteLine("  UniversalDeployer.exe update    --config \"path\\to\\appsettings.json\"");
            Console.WriteLine("  UniversalDeployer.exe uninstall --config \"path\\to\\appsettings.json\"");
        }

        static string GetArg(string[] args, string name)
        {
            var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && i + 1 < args.Length) return args[i + 1];
            return null;
        }

        static void EnsureAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(id);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("This operation requires Administrator privileges.");
            }
        }

        static void Install(AppRoot root)
        {
            PrepareDirectories(root.App);
            StopServices(root);
            StopProcesses(root.App);

            CopyFromSource(root.App);

            ConfigureServices(root, createIfMissing: true, startAfter: true);
            RunPostInstall(root.App);
        }

        static void Update(AppRoot root)
        {
            PrepareDirectories(root.App);
            StopServices(root);
            StopProcesses(root.App);

            CopyFromSource(root.App);

            ConfigureServices(root, createIfMissing: true, startAfter: true);
            RunPostInstall(root.App);
        }

        static void Uninstall(AppRoot root)
        {
            StopServices(root);
            RemoveServices(root);

            // Clean files
            var tgt = Normalize(root.App.TargetDir);
            if (Directory.Exists(tgt))
            {
                Console.WriteLine("Cleaning target directory: " + tgt);
                // delete everything except preserved patterns; also delete PostUninstallDelete
                var allFiles = Directory.GetFiles(tgt, "*", SearchOption.AllDirectories);
                foreach (var f in allFiles)
                {
                    var rel = MakeRelative(tgt, f);
                    if (IsPreserved(rel, root.App.Preserve))
                        continue;
                    TryDeleteFile(f);
                }
                foreach (var pattern in root.App.PostUninstallDelete ?? Enumerable.Empty<string>())
                {
                    foreach (var path in Directory.GetFiles(tgt, "*", SearchOption.AllDirectories))
                    {
                        var rel = MakeRelative(tgt, path);
                        if (Glob.IsMatch(pattern, rel)) TryDeleteFile(path);
                    }
                    foreach (var dir in Directory.GetDirectories(tgt, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                    {
                        var rel = MakeRelative(tgt, dir);
                        if (Glob.IsMatch(pattern, rel) && IsDirEmpty(dir))
                        {
                            TryDeleteDir(dir);
                        }
                    }
                }

                // remove empty dirs
                foreach (var dir in Directory.GetDirectories(tgt, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                {
                    if (IsDirEmpty(dir)) TryDeleteDir(dir);
                }
            }
        }

        static void PrepareDirectories(AppConfig app)
        {
            var src = Path.GetFullPath(Environment.ExpandEnvironmentVariables(app.SourceDir ?? "."));
            var tgt = Normalize(app.TargetDir);
            if (!Directory.Exists(src))
                throw new DirectoryNotFoundException("SourceDir not found: " + src);
            Directory.CreateDirectory(tgt);
            Console.WriteLine("Source: " + src);
            Console.WriteLine("Target: " + tgt);
        }

        static void StopProcesses(AppConfig app)
        {
            foreach (var name in app.StopProcesses ?? Enumerable.Empty<string>())
            {
                var procName = Path.GetFileNameWithoutExtension(name);
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    try
                    {
                        Console.WriteLine("Killing process: " + p.ProcessName + " (PID " + p.Id + ")");
                        p.Kill();
                        p.WaitForExit(10000);
                    }
                    catch { }
                }
            }
        }

        static void CopyFromSource(AppConfig app)
        {
            var src = Path.GetFullPath(Environment.ExpandEnvironmentVariables(app.SourceDir ?? "."));
            var tgt = Normalize(app.TargetDir);

            if (app.CleanTarget)
            {
                Console.WriteLine("Cleaning target (respecting Preserve globs)...");
                var all = Directory.GetFiles(tgt, "*", SearchOption.AllDirectories);
                foreach (var f in all)
                {
                    var rel = MakeRelative(tgt, f);
                    // If that relative path exists in source, we will overwrite later; but if not in source and not preserved, delete it.
                    var srcPath = Path.Combine(src, rel);
                    if (!File.Exists(srcPath) && !IsPreserved(rel, app.Preserve))
                        TryDeleteFile(f);
                }
            }

            Console.WriteLine("Copying files...");
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = MakeRelative(src, f);
                var dest = Path.Combine(tgt, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(f, dest, true);
            }
        }

        static bool IsPreserved(string relativePath, System.Collections.Generic.IEnumerable<string> patterns)
        {
            relativePath = relativePath.Replace('/', '\\');
            foreach (var p in patterns ?? Enumerable.Empty<string>())
            {
                if (Glob.IsMatch(p.Replace('/', '\\'), relativePath))
                    return true;
            }
            return false;
        }

        static void RunPostInstall(AppConfig app)
        {
            foreach (var cmd in app.PostInstallRun ?? Enumerable.Empty<string>())
            {
                try
                {
                    Console.WriteLine("PostInstall: " + cmd);
                    var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var p = Process.Start(psi);
                    p.WaitForExit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[WARN] PostInstall failed: " + ex.Message);
                }
            }
        }

        static void ConfigureServices(AppRoot root, bool createIfMissing, bool startAfter)
        {
            var tgt = Normalize(root.App.TargetDir);
            foreach (var svc in root.Services ?? Enumerable.Empty<ServiceConfig>())
            {
                bool exists = ServiceExists(svc.Name);
                var exeFull = Path.Combine(tgt, svc.Exe ?? "");

                if (!exists && !createIfMissing)
                {
                    Console.WriteLine($"[WARN] Service {svc.Name} not found. Skipping.");
                    continue;
                }

                if (!exists)
                {
                    Console.WriteLine("Creating service " + svc.Name);
                    RunSc($"create \"{svc.Name}\" binPath= \"{exeFull}\" start= {NormalizeStartType(svc.StartType)} DisplayName= \"{svc.DisplayName ?? svc.Name}\"");
                    if (!string.IsNullOrWhiteSpace(svc.Description))
                        RunSc($"description \"{svc.Name}\" \"{svc.Description}\"");
                }
                else
                {
                    Console.WriteLine("Service exists: " + svc.Name + " (will update binary path, start type, description)");
                    RunSc($"config \"{svc.Name}\" binPath= \"{exeFull}\" start= {NormalizeStartType(svc.StartType)}");
                    if (!string.IsNullOrWhiteSpace(svc.DisplayName))
                        RunSc($"config \"{svc.Name}\" DisplayName= \"{svc.DisplayName}\"");
                    if (!string.IsNullOrWhiteSpace(svc.Description))
                        RunSc($"description \"{svc.Name}\" \"{svc.Description}\"");
                }

                if (!string.Equals(svc.Account ?? "LocalSystem", "LocalSystem", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Configuring logon account...");
                    RunSc($"config \"{svc.Name}\" obj= \"{svc.Account}\" password= \"{svc.Password ?? ""}\"");
                }

                if (!string.IsNullOrWhiteSpace(svc.FailureActions))
                {
                    RunSc($"failure \"{svc.Name}\" {svc.FailureActions}");
                }

                Console.WriteLine("Stopping service (if running)...");
                RunSc($"stop \"{svc.Name}\"", ignoreErrors: true);
                // wait a bit
                System.Threading.Thread.Sleep(1000);

                if (startAfter)
                {
                    Console.WriteLine("Starting service...");
                    RunSc($"start \"{svc.Name}\"", ignoreErrors: false);
                }
            }
        }

        
static void StopServices(AppRoot root)
{
    foreach (var svc in root.Services ?? Enumerable.Empty<ServiceConfig>())
    {
        try
        {
            if (!ServiceExists(svc.Name)) continue;
            Console.WriteLine("Stopping service " + svc.Name + "...");
            RunSc($"stop \"{svc.Name}\"", ignoreErrors: true);

            // Wait up to ~20s for it to stop
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < 20)
            {
                try
                {
                    var sc = new ServiceController(svc.Name);
                    sc.Refresh();
                    if (sc.Status == ServiceControllerStatus.Stopped) break;
                }
                catch { break; } // service might be gone
                System.Threading.Thread.Sleep(1000);
            }
        }
        catch { /* swallow to continue with others */ }
    }
}

static void RemoveServices(AppRoot root)
        {
            foreach (var svc in root.Services ?? Enumerable.Empty<ServiceConfig>())
            {
                if (ServiceExists(svc.Name))
                {
                    Console.WriteLine("Stopping " + svc.Name);
                    RunSc($"stop \"{svc.Name}\"", ignoreErrors: true);
                    System.Threading.Thread.Sleep(1000);
                    Console.WriteLine("Deleting " + svc.Name);
                    RunSc($"delete \"{svc.Name}\"", ignoreErrors: true);
                }
            }
        }

        static bool ServiceExists(string name)
        {
            try
            {
                return ServiceController.GetServices().Any(s => s.ServiceName.Equals(name, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        static string Normalize(string path) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path ?? ".").Replace('/', '\\'));

        static string MakeRelative(string root, string path)
        {
            root = Normalize(root).TrimEnd('\\') + "\\";
            path = Normalize(path);
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return path.Substring(root.Length);
            return path;
        }

        static void TryDeleteFile(string f)
        {
            try { File.Delete(f); } catch { }
        }
        static void TryDeleteDir(string d)
        {
            try { Directory.Delete(d, false); } catch { }
        }
        static bool IsDirEmpty(string path)
        {
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }

        static string NormalizeStartType(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "auto";
            s = s.Trim().ToLowerInvariant();
            if (s.StartsWith("auto")) return "auto";
            if (s.StartsWith("demand") || s.StartsWith("manual")) return "demand";
            if (s.StartsWith("disab")) return "disabled";
            return "auto";
        }

        static void RunSc(string args, bool ignoreErrors = FalseLiteral)
        {
            var psi = new ProcessStartInfo("sc.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var p = Process.Start(psi);
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 && !ignoreErrors)
                throw new InvalidOperationException("sc.exe " + args + " failed: " + output + " " + error);
            if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output.Trim());
            if (!string.IsNullOrWhiteSpace(error)) Console.WriteLine(error.Trim());
        }

        // Workaround because we can't use default parameter with literal 'false' in string method above cleanly in older compilers
        private const bool FalseLiteral = false;
    }
}
