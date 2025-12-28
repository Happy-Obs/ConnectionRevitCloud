using System.Net.NetworkInformation;

namespace ConnectionRevitCloud.Client.Services;

public class DiagnosticsService
{
    private readonly WireGuardWindowsService _wg;
    private readonly LocalLogger _log;

    public DiagnosticsService(WireGuardWindowsService wg, LocalLogger log)
    {
        _wg = wg;
        _log = log;
    }

    public async Task<long?> PingAsync(string ip)
    {
        try
        {
            if (!_wg.IsRunning()) return null;
            using var p = new Ping();
            var r = await p.SendPingAsync(ip, 1000);
            return r.Status == IPStatus.Success ? r.RoundtripTime : null;
        }
        catch (Exception ex)
        {
            _log.Error("Ping failed", ex);
            return null;
        }
    }
}
