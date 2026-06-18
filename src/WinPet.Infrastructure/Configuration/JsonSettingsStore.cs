using System.Text.Json;
using WinPet.Core.Configuration;

namespace WinPet.Infrastructure.Configuration;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _settingsPath;

    public JsonSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public async Task<WinPetSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new WinPetSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<WinPetSettings>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            settings ??= new WinPetSettings();
            settings.Validate();
            return settings;
        }
        catch (JsonException)
        {
            return new WinPetSettings();
        }
        catch (ArgumentOutOfRangeException)
        {
            return new WinPetSettings();
        }
    }

    public async Task SaveAsync(
        WinPetSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings.Validate();
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException(
                "Settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
