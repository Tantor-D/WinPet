using System.Text.Json;
using WinPet.Core.Pets;

namespace WinPet.Infrastructure.Pets;

public sealed class CodexPetCatalog : ICodexPetCatalog
{
    private const int AtlasWidth = 1536;
    private const int AtlasHeight = 1872;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _petsDirectory;

    public CodexPetCatalog(string? codexHome = null)
    {
        var home = codexHome;
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("CODEX_HOME");
        }

        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".codex");
        }

        _petsDirectory = Path.Combine(Path.GetFullPath(home), "pets");
    }

    public IReadOnlyList<CodexPetDefinition> Discover()
    {
        if (!Directory.Exists(_petsDirectory))
        {
            return [];
        }

        var pets = new List<CodexPetDefinition>();
        foreach (var directory in Directory.EnumerateDirectories(
                     _petsDirectory))
        {
            var pet = TryLoad(directory);
            if (pet is not null)
            {
                pets.Add(pet);
            }
        }

        return pets
            .OrderBy(pet => pet.Manifest.DisplayName)
            .ToArray();
    }

    public CodexPetDefinition? Find(string id) =>
        Discover().FirstOrDefault(
            pet => string.Equals(
                pet.Manifest.Id,
                id,
                StringComparison.OrdinalIgnoreCase));

    private static CodexPetDefinition? TryLoad(string packageDirectory)
    {
        try
        {
            var manifestPath = Path.Combine(packageDirectory, "pet.json");
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            var manifest = JsonSerializer.Deserialize<CodexPetManifest>(
                File.ReadAllText(manifestPath),
                SerializerOptions);
            if (manifest is null ||
                string.IsNullOrWhiteSpace(manifest.Id) ||
                string.IsNullOrWhiteSpace(manifest.DisplayName) ||
                string.IsNullOrWhiteSpace(manifest.SpritesheetPath))
            {
                return null;
            }

            var packageRoot = Path.GetFullPath(packageDirectory);
            var spritesheet = Path.GetFullPath(
                Path.Combine(packageRoot, manifest.SpritesheetPath));
            if (!spritesheet.StartsWith(
                    packageRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(spritesheet))
            {
                return null;
            }

            var extension = Path.GetExtension(spritesheet);
            if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var stream = File.OpenRead(spritesheet);
            using var bitmap = SkiaSharp.SKBitmap.Decode(stream);
            if (bitmap is null ||
                bitmap.Width != AtlasWidth ||
                bitmap.Height != AtlasHeight)
            {
                return null;
            }

            return new CodexPetDefinition(
                manifest,
                packageRoot,
                spritesheet);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
