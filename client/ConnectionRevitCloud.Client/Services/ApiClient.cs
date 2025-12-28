using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace ConnectionRevitCloud.Client.Services;

public class ApiClient
{
    private readonly HttpClient _http;
    private readonly string _pinnedSha256;

    public ApiClient(string baseUrl, string pinnedCertSha256)
    {
        _pinnedSha256 = (pinnedCertSha256 ?? "").Replace(":", "").Replace(" ", "").ToUpperInvariant();

        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
        {
            if (string.IsNullOrWhiteSpace(_pinnedSha256))
            {
                // На старте пока пусто — временно НЕ блочим. Как только получишь fingerprint — вставим и включим строгость.
                return true;
            }

            if (cert is null) return false;
            var x = new X509Certificate2(cert);
            var sha256 = x.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
            return sha256.Equals(_pinnedSha256, StringComparison.OrdinalIgnoreCase);
        };

        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<string> Login(string username, string password)
    {
        var payload = JsonSerializer.Serialize(new { Username = username, Password = password });
        var res = await _http.PostAsync("/api/v1/login", new StringContent(payload, Encoding.UTF8, "application/json"));
        if (!res.IsSuccessStatusCode) throw new Exception("Неверный логин/пароль или пользователь отключен.");
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    public async Task<string> GetConfig(string jwt)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/config");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<(string version, string installerUrl)> GetLatest()
    {
        var res = await _http.GetAsync("/api/v1/client/latest");
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return (
            doc.RootElement.GetProperty("version").GetString() ?? "1.0.0",
            doc.RootElement.GetProperty("installerUrl").GetString() ?? ""
        );
    }
}
