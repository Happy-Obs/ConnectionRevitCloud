using System.Diagnostics;
using System.Text;
using ConnectionRevitCloud.Server.Data;
using ConnectionRevitCloud.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
private const string Bash = "/bin/bash";
private const string Wg = "/usr/bin/wg";
private const string WgQuick = "/usr/bin/wg-quick";

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

    public async Task CreateUserWithGeneratedConfig(string username, string password, string Wgip)
    {
        // 1) ключи клиента
        var clientPriv = RunAndCapture("Wg", "genkey").Trim();
        var clientPub = RunAndCapture("Bash", $"-lc \"echo '{clientPriv}' | Wg pubkey\"").Trim();

        // 2) public key сервера
        var iface = _cfg["WireGuard:Interface"] ?? "wg0";
        var serverPub = RunAndCapture("Wg", $"show {iface} public-key").Trim();

        // 3) шаблон клиента (как у тебя)
        var endpointHost = _cfg["WireGuard:EndpointHost"] ?? "87.242.103.223";
        var endpointPort = int.Parse(_cfg["WireGuard:EndpointPort"] ?? "36953");
        var allowed = _cfg["WireGuard:ClientAllowedIps"] ?? "10.10.0.0/24";
        var keepalive = int.Parse(_cfg["WireGuard:PersistentKeepalive"] ?? "25");

        var clientCfg = $@"[Interface]
PrivateKey = {clientPriv}
Address = {Wgip}/32

[Peer]
PublicKey = {serverPub}
AllowedIPs = {allowed}
Endpoint = {endpointHost}:{endpointPort}
PersistentKeepalive = {keepalive}
";

        // 4) сохранить конфиг и ключи в /root/Wg-clients/<username>/
	var baseDir = _cfg["WireGuard:ClientConfigsDir"] ?? "/root/Wg-clients";
	var userDir = Path.Combine(baseDir, username);
	Directory.CreateDirectory(userDir);

	var privPath = Path.Combine(userDir, $"{username}.private.key");
	var pubPath  = Path.Combine(userDir, $"{username}.public.key");
	var confPath = Path.Combine(userDir, $"{username}.conf");

	await File.WriteAllTextAsync(privPath, clientPriv + "\n", Encoding.UTF8);
	await File.WriteAllTextAsync(pubPath, clientPub + "\n", Encoding.UTF8);
	await File.WriteAllTextAsync(confPath, clientCfg, Encoding.UTF8);

	// далее используем confPath как ConfigPath
	var clientConfigPath = confPath;


        // 5) добавить peer в wg0.conf
        AddPeerToServerConfig(clientPub, $"{Wgip}/32");

        // 6) применить без даунтайма
        ApplyServerConfigNoDowntime();

        // 7) user в БД
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var u = new User
        {
            Username = username,
            PasswordHash = hash,
            WgIp = Wgip,
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

        // cfgPath = /root/Wg-clients/<user>/<user>.conf
	var dir = Path.GetDirectoryName(cfgPath);
	if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
    		Directory.Delete(dir, recursive: true);
	else if (File.Exists(cfgPath))
    		File.Delete(cfgPath);

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
        RunAndCapture("Bash", $"-lc \"Wg syncconf {iface} <(WgQuick strip {iface})\"");
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
