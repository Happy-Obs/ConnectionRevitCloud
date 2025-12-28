using System.Reflection;

namespace ConnectionRevitCloud.Client.Services;

public class UpdateChecker
{
    private readonly ApiClient _api;
    private readonly LocalLogger _log;

    public UpdateChecker(ApiClient api, LocalLogger log)
    {
        _api = api;
        _log = log;
    }

    public async Task<string> CheckAsync()
    {
        try
        {
            var (latest, url) = await _api.GetLatest();
            var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

            if (IsNewer(latest, current))
                return $"Доступно обновление: {latest}. Скачай: {url}";

            return "Обновлений нет.";
        }
        catch (Exception ex)
        {
            _log.Error("Update check failed", ex);
            return "";
        }
    }

    private static bool IsNewer(string a, string b)
    {
        Version va = Version.TryParse(a, out var x) ? x : new Version(0, 0, 0);
        Version vb = Version.TryParse(b, out var y) ? y : new Version(0, 0, 0);
        return va > vb;
    }
}
