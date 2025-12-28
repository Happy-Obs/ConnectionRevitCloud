using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConnectionRevitCloud.Client.Services;

namespace ConnectionRevitCloud.Client.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // === Настройки (позже вынесем в config.json) ===
    private const string BaseUrl = "https://92.51.22.225";
    private const string TunnelName = "ConnectionRevitCloud";
    // Пока пусто: после установки сервера возьмёшь SHA256 fingerprint и вставим сюда.
    private const string PinnedCertSha256 = "8963D955F639675C1683A7DE94A66623B1F1CED1EC51437E411495CF4C783313";

    private readonly ApiClient _api;
    private readonly WireGuardWindowsService _wg;
    private readonly DiagnosticsService _diag;
    private readonly UpdateChecker _upd;
    private readonly LocalLogger _log;

    private string _username = "";
    private string? _token;
    private string? _wgIp;
    private string? _configPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }

    private string _statusLine = "Статус: отключено";
    public string StatusLine { get => _statusLine; set { _statusLine = value; OnPropertyChanged(); } }

    private string _detailsLine = "";
    public string DetailsLine { get => _detailsLine; set { _detailsLine = value; OnPropertyChanged(); } }

    private string _trafficLine = "";
    public string TrafficLine { get => _trafficLine; set { _trafficLine = value; OnPropertyChanged(); } }

    private string _pingLine = "";
    public string PingLine { get => _pingLine; set { _pingLine = value; OnPropertyChanged(); } }

    private string _updateLine = "";
    public string UpdateLine { get => _updateLine; set { _updateLine = value; OnPropertyChanged(); } }

    public MainViewModel()
    {
        _log = new LocalLogger();
        _api = new ApiClient(BaseUrl, PinnedCertSha256);
        _wg = WireGuardWindowsService.CreateDefault(TunnelName, _log);
        _diag = new DiagnosticsService(_wg, _log);
        _upd = new UpdateChecker(_api, _log);

        _ = LoopRefreshAsync();
        _ = LoopUpdatesAsync();
    }

    public async Task ConnectAsync(string password)
    {
        try
        {
            StatusLine = "Статус: подключение...";
            _token = await _api.Login(Username.Trim(), password);

            var configText = await _api.GetConfig(_token);

            _wgIp = ParseAddressFromConfig(configText);
            _configPath = _wg.SaveConfigToWorkdir(configText);

            _wg.InstallAndStartTunnel(_configPath);

            await Task.Delay(700);
            await RefreshNowAsync();
        }
        catch (Exception ex)
        {
            _log.Error("Connect failed", ex);
            StatusLine = "Статус: ошибка";
            DetailsLine = ex.Message;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _wg.StopAndUninstallTunnel();
            await Task.Delay(500);
            await RefreshNowAsync();
        }
        catch (Exception ex)
        {
            _log.Error("Disconnect failed", ex);
            StatusLine = "Статус: ошибка";
            DetailsLine = ex.Message;
        }
    }

    public bool IsConnected() => _wg.IsRunning();

    public async Task RefreshNowAsync()
    {
        var running = _wg.IsRunning();
        if (!running)
        {
            StatusLine = "Статус: отключено";
            DetailsLine = "";
            TrafficLine = "";
            PingLine = "";
            return;
        }

        StatusLine = "Статус: подключено";
        DetailsLine = $"Логин: {Username} | WG IP: {_wgIp ?? "?"}";

        var stats = _wg.TryGetStats();
        if (stats is not null)
        {
            var (rx, tx, hsAge) = stats.Value;
            var hs = hsAge is null ? "нет" : $"{(int)hsAge.Value.TotalSeconds} сек назад";
            TrafficLine = $"Трафик: ↓ {FormatBytes(rx)} / ↑ {FormatBytes(tx)} | Handshake: {hs}";
        }
        else
        {
            TrafficLine = "Трафик: недоступно";
        }

        var pingMs = await _diag.PingAsync("10.10.0.1"); // можно поменять на твой WG сервер IP
        PingLine = pingMs is null ? "Ping: нет ответа" : $"Ping: {pingMs} ms";
    }

    private async Task LoopRefreshAsync()
    {
        while (true)
        {
            try { await RefreshNowAsync(); }
            catch (Exception ex) { _log.Error("Refresh loop", ex); }
            await Task.Delay(1500);
        }
    }

    private async Task LoopUpdatesAsync()
    {
        while (true)
        {
            try
            {
                var msg = await _upd.CheckAsync();
                if (!string.IsNullOrWhiteSpace(msg))
                    UpdateLine = msg;
            }
            catch (Exception ex) { _log.Error("Update loop", ex); }

            await Task.Delay(TimeSpan.FromMinutes(10));
        }
    }

    private static string? ParseAddressFromConfig(string cfg)
    {
        // Address = 10.10.0.30/32
        var m = Regex.Match(cfg, @"^\s*Address\s*=\s*([0-9\.]+)/32\s*$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string FormatBytes(long b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double d = b;
        int i = 0;
        while (d >= 1024 && i < u.Length - 1) { d /= 1024; i++; }
        return $"{d:0.##} {u[i]}";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
