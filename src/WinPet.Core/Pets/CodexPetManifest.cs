namespace WinPet.Core.Pets;

public sealed record CodexPetManifest
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string Description { get; init; } = string.Empty;

    public required string SpritesheetPath { get; init; }
}
