using System.Diagnostics;
using System.ServiceProcess;

namespace ConnectionRevitCloud.Client.Services;

public class WireGuardWindowsService
{
    private readonly LocalLogger _log;

    public string TunnelName { get; }
    public string WireGuardExePath { get; }
    public string WgExePath { get; }
    public string WorkDir { get; }

    private WireGuardWindowsService(string tunnelName, string wireguardExePath, string wgExePath, string workDir, LocalLogger log)
    {
        TunnelName = tunnelName;
        WireGuardExePath = wireguardExePath;
        WgExePath = wgExePath;
        WorkDir = workDir;
        _log = log;
    }

    public static WireGuardWindowsService CreateDefault(string tunnelName, LocalLogger log)
    {
        // Обычно WireGuard ставится сюда:
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var wgDir = Path.Combine(programFiles, "WireGuard");
        var wireguardExe = Path.Combine(wgDir, "wireguard.exe");
        var wgExe = Path.Combine(wgDir, "wg.exe");

        var workDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConnectionRevitCloud", "wg");

        Directory.CreateDirectory(workDir);

        return new WireGuardWindowsService(tunnelName, wireguardExe, wgExe, workDir, log);
    }

    public string SaveConfigToWorkdir(string configText)
    {
        var path = Path.Combine(WorkDir, $"{TunnelName}.conf");
        File.WriteAllText(path, configText);
        return path;
    }

    public void InstallAndStartTunnel(string configPath)
    {
        if (!File.Exists(WireGuardExePath))
            throw new FileNotFoundException("WireGuard не установлен (wireguard.exe не найден).");

        RunElevated(WireGuardExePath, $"/installtunnelservice \"{configPath}\"");
    }

    public void StopAndUninstallTunnel()
    {
        if (!File.Exists(WireGuardExePath))
            return;

        RunElevated(WireGuardExePath, $"/uninstalltunnelservice {TunnelName}");
    }

    public bool IsRunning()
    {
        var serviceName = $"WireGuardTunnel${TunnelName}";
        try
        {
            using var sc = new ServiceController(serviceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch { return false; }
    }

    public (long rxBytes, long txBytes, TimeSpan? latestHandshakeAge)? TryGetStats()
    {
        try
        {
            if (!File.Exists(WgExePath)) return null;
            var output = RunCapture(WgExePath, $"show {TunnelName} dump");

            long rx = 0, tx = 0;
            TimeSpan? hsAge = null;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines.Skip(1))
            {
                var cols = line.Split('\t');
                if (cols.Length >= 8)
                {
                    if (long.TryParse(cols[5], out var r)) rx += r;
                    if (long.TryParse(cols[6], out var t)) tx += t;

                    if (long.TryParse(cols[4], out var hsUnix) && hsUnix > 0)
                    {
                        var hs = DateTimeOffset.FromUnixTimeSeconds(hsUnix);
                        var age = DateTimeOffset.UtcNow - hs;
                        if (hsAge is null || age < hsAge) hsAge = age;
                    }
                }
            }

            return (rx, tx, hsAge);
        }
        catch (Exception ex)
        {
            _log.Error("Stats failed", ex);
            return null;
        }
    }

    private static void RunElevated(string file, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = true,
            Verb = "runas"
        };
        using var p = Process.Start(psi);
        p?.WaitForExit();
    }

    private static string RunCapture(string file, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception(stderr);
        return stdout;
    }
}
