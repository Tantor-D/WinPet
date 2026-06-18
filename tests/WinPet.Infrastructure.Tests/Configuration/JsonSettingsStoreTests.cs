using WinPet.Core.Configuration;
using WinPet.Infrastructure.Configuration;

namespace WinPet.Infrastructure.Tests.Configuration;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task Settings_round_trip_through_json()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinPet.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new JsonSettingsStore(path);
            var expected = new WinPetSettings
            {
                MaximumWorkMinutes = 55,
                QualifiedBreakMinutes = 7,
                ThemeName = "lucy",
            };

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
