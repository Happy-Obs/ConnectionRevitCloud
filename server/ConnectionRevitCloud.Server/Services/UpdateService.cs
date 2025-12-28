namespace ConnectionRevitCloud.Server.Services;

public class UpdateService
{
    private readonly IConfiguration _cfg;
    public UpdateService(IConfiguration cfg) => _cfg = cfg;

    public object GetLatest() => new
    {
        version = _cfg["Updates:LatestVersion"] ?? "1.0.0",
        installerUrl = _cfg["Updates:InstallerUrl"] ?? ""
    };
}
