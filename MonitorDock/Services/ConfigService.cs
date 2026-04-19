using System.IO;
using System.Text.Json;
using MonitorDock.Models;

namespace MonitorDock.Services;

public static class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorDock");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly string BackupPath = Path.Combine(ConfigDir, "config.json.bak");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppConfig Load()
    {
        var config = TryLoad(ConfigPath);
        if (config != null) return config;

        // Primary config missing or corrupt — try backup
        config = TryLoad(BackupPath);
        if (config != null)
        {
            // Restore backup as primary
            try { File.Copy(BackupPath, ConfigPath, true); } catch { }
            return config;
        }

        return new AppConfig();
    }

    private static AppConfig? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);

        // Backup existing config before overwriting
        if (File.Exists(ConfigPath))
        {
            try { File.Copy(ConfigPath, BackupPath, true); } catch { }
        }

        // Atomic write: write to temp file, then replace
        var tempPath = ConfigPath + ".tmp";
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigPath, true);
    }
}
