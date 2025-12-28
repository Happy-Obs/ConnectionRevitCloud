using System.Text;

namespace ConnectionRevitCloud.Client.Services;

public class LocalLogger
{
    private readonly string _path;

    public LocalLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConnectionRevitCloud", "logs");

        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"log_{DateTime.Now:yyyyMMdd}.txt");
    }

    public void Info(string msg) => Write("INFO", msg);
    public void Error(string msg, Exception ex) => Write("ERR", $"{msg}\n{ex}");

    private void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:O} [{level}] {msg}\n";
        File.AppendAllText(_path, line, Encoding.UTF8);
    }
}
