using System.Text.Json;
using SkiaSharp;
using WinPet.Core.Pets;
using WinPet.Infrastructure.Pets;

namespace WinPet.Infrastructure.Tests.Pets;

public sealed class CodexPetCatalogTests
{
    [Fact]
    public void Discovers_an_exact_codex_pet_package()
    {
        var codexHome = CreateCodexHome();
        try
        {
            CreatePet(codexHome, "test-pet", 1536, 1872);
            var catalog = new CodexPetCatalog(codexHome);

            var pet = Assert.Single(catalog.Discover());

            Assert.Equal("test-pet", pet.Manifest.Id);
            Assert.EndsWith(
                "spritesheet.png",
                pet.SpritesheetFullPath,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Fact]
    public void Rejects_a_spritesheet_with_non_codex_dimensions()
    {
        var codexHome = CreateCodexHome();
        try
        {
            CreatePet(codexHome, "wrong-size", 192, 208);
            var catalog = new CodexPetCatalog(codexHome);

            Assert.Empty(catalog.Discover());
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    private static string CreateCodexHome()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinPet.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CreatePet(
        string codexHome,
        string id,
        int width,
        int height)
    {
        var directory = Path.Combine(codexHome, "pets", id);
        Directory.CreateDirectory(directory);
        var manifest = new CodexPetManifest
        {
            Id = id,
            DisplayName = "Test Pet",
            Description = "Test",
            SpritesheetPath = "spritesheet.png",
        };
        File.WriteAllText(
            Path.Combine(directory, "pet.json"),
            JsonSerializer.Serialize(manifest));

        using var bitmap = new SKBitmap(width, height, isOpaque: false);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(
            Path.Combine(directory, "spritesheet.png"));
        data.SaveTo(output);
    }
}
