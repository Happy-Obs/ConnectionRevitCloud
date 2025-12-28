namespace ConnectionRevitCloud.Server.Data.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsEnabled { get; set; } = true;

    public string WgIp { get; set; } = "";       // 10.10.0.X
    public string ConfigPath { get; set; } = ""; // /opt/connectionrevitcloud/configs/<user>.conf or custom

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
