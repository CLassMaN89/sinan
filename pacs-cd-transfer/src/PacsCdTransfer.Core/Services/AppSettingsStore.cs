using System.Text.Json;
using PacsCdTransfer.Core.Models;

namespace PacsCdTransfer.Core.Services;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as JSON next to the executable. Seeds a default
/// admin/admin account on first run, matching the mockup's fixed initial login.
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ConfigPath { get; }

    public AppSettingsStore(string? configPath = null)
    {
        ConfigPath = configPath ?? Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var fresh = CreateDefault();
            Save(fresh);
            return fresh;
        }

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefault();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    private static AppSettings CreateDefault() => new()
    {
        Users =
        {
            new UserAccount
            {
                Username = "admin",
                PasswordHash = PasswordHasher.Hash("admin"),
                IsAdmin = true,
                CanTransferCd = true,
                CanQueryRetrieve = true
            }
        }
    };
}
