namespace WinPet.Core.Configuration;

public interface ISettingsStore
{
    Task<WinPetSettings> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        WinPetSettings settings,
        CancellationToken cancellationToken = default);
}
