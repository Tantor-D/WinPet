namespace WinPet.Core.Pets;

public sealed record CodexPetDefinition(
    CodexPetManifest Manifest,
    string PackageDirectory,
    string SpritesheetFullPath);
