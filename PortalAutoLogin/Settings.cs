using System.Text.Json;
using Microsoft.Win32;

namespace PortalAutoLogin;

internal sealed class Settings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "PortalAutoLogin";
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortalAutoLogin");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public string UserName { get; set; } = "";
    public bool StartWithWindows { get; set; } = true;

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()
                : new Settings();
        }
        catch (Exception)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (StartWithWindows)
            key.SetValue(RunName, $"\"{Application.ExecutablePath}\" --background");
        else
            key.DeleteValue(RunName, throwOnMissingValue: false);
    }
}
