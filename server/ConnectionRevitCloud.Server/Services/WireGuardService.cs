using System.Diagnostics;
using System.Text;
using ConnectionRevitCloud.Server.Data;
using ConnectionRevitCloud.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ConnectionRevitCloud.Server.Services;

public class WireGuardService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;

    public WireGuardService(AppDbContext db, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    public async Task<string> GetClientConfigText(User u)
    {
        if (!File.Exists(u.ConfigPath))
            throw new FileNotFoundException($"Config not found: {u.ConfigPath}");

        return await File.ReadAllTextAsync(u.ConfigPath, Encoding.UTF8);
    }

    public async Task CreateUserWithGeneratedConfig(string username, string password, string wgip)
    {
        // 1) ключи клиента
        var clientPriv = RunAndCapture("wg", "genkey").Trim();
        var clientPub = RunAndCapture("bash", $"-lc \"echo '{clientPriv}' | wg pubkey\"").Trim();

        // 2) public key сервера
        var iface = _cfg["WireGuard:Interface"] ?? "wg0";
        var serverPub = RunAndCapture("wg", $"show {iface} public-key").Trim();

        // 3) шаблон клиента (как у тебя)
        var endpointHost = _cfg["WireGuard:EndpointHost"] ?? "87.242.103.223";
        var endpointPort = int.Parse(_cfg["WireGuard:EndpointPort"] ?? "36953");
        var allowed = _cfg["WireGuard:ClientAllowedIps"] ?? "10.10.0.0/24";
        var keepalive = int.Parse(_cfg["WireGuard:PersistentKeepalive"] ?? "25");

        var clientCfg = $@"[Interface]
PrivateKey = {clientPriv}
Address = {wgip}/32

[Peer]
PublicKey = {serverPub}
AllowedIPs = {allowed}
Endpoint = {endpointHost}:{endpointPort}
PersistentKeepalive = {keepalive}
";

        // 4) сохранить конфиг
        var dir = _cfg["WireGuard:ClientConfigsDir"] ?? "/opt/connectionrevitcloud/configs";
        Directory.CreateDirectory(dir);
        var clientConfigPath = Path.Combine(dir, $"{username}.conf");
        await File.WriteAllTextAsync(clientConfigPath, clientCfg, Encoding.UTF8);

        // 5) добавить peer в wg0.conf
        AddPeerToServerConfig(clientPub, $"{wgip}/32");

        // 6) применить без даунтайма
        ApplyServerConfigNoDowntime();

        // 7) user в БД
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var u = new User
        {
            Username = username,
            PasswordHash = hash,
            WgIp = wgip,
            ConfigPath = clientConfigPath,
            IsEnabled = true
        };
        _db.Users.Add(u);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteUser(string username, bool deleteConfigAndPeer)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Username == username);
        if (u is null) return;

        var cfgPath = u.ConfigPath;
        var allowedIp = $"{u.WgIp}/32";

        _db.Users.Remove(u);
        await _db.SaveChangesAsync();

        if (!deleteConfigAndPeer) return;

        if (File.Exists(cfgPath)) File.Delete(cfgPath);

        RemovePeerFromServerConfigByAllowedIp(allowedIp);
        ApplyServerConfigNoDowntime();
    }

    private void AddPeerToServerConfig(string clientPublicKey, string allowedIpCidr)
    {
        var path = _cfg["WireGuard:ServerConfigPath"] ?? "/etc/wireguard/wg0.conf";
        var text = File.ReadAllText(path);

        var peerBlock = $@"

# CRC {DateTime.UtcNow:O}
[Peer]
PublicKey = {clientPublicKey}
AllowedIPs = {allowedIpCidr}
";

        File.WriteAllText(path, text.TrimEnd() + peerBlock + Environment.NewLine, Encoding.UTF8);
    }

    private void RemovePeerFromServerConfigByAllowedIp(string allowedIpCidr)
    {
        var path = _cfg["WireGuard:ServerConfigPath"] ?? "/etc/wireguard/wg0.conf";
        var lines = File.ReadAllLines(path).ToList();

        var result = new List<string>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals("[Peer]", StringComparison.OrdinalIgnoreCase))
            {
                int end = i;
                bool match = false;

                for (int j = i; j < lines.Count; j++)
                {
                    var t = lines[j].Trim();
                    if (j > i && t.StartsWith("[") && t.EndsWith("]"))
                    {
                        end = j - 1;
                        break;
                    }
                    if (j == lines.Count - 1) end = j;

                    if (t.StartsWith("AllowedIPs", StringComparison.OrdinalIgnoreCase) &&
                        t.Contains(allowedIpCidr, StringComparison.OrdinalIgnoreCase))
                        match = true;
                }

                if (match)
                {
                    i = end;
                    continue;
                }
            }

            result.Add(lines[i]);
        }

        File.WriteAllLines(path, result);
    }

    private void ApplyServerConfigNoDowntime()
    {
        var iface = _cfg["WireGuard:Interface"] ?? "wg0";
        RunAndCapture("bash", $"-lc \"wg syncconf {iface} <(wg-quick strip {iface})\"");
    }

    private static string RunAndCapture(string file, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0)
            throw new Exception($"Command failed: {file} {args}\n{stderr}");

        return stdout;
    }
}
