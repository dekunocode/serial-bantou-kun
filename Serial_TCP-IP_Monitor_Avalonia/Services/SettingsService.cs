using System.Text.Json;
using Serial_TCP_IP_Monitor_Avalonia.Models;
using Serial_TCP_IP_Monitor_Avalonia.Serialization;

namespace Serial_TCP_IP_Monitor_Avalonia.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SerialBantouKun");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize(File.ReadAllText(_settingsPath), AppJsonContext.Default.AppSettings) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception) when (File.Exists(_settingsPath))
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings));
        }
        catch (Exception)
        {
            // 設定保存に失敗してもアプリ終了は妨げない。
        }
    }
}
