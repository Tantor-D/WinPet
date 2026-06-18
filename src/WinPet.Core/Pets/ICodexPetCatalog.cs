namespace WinPet.Core.Pets;

public interface ICodexPetCatalog
{
    IReadOnlyList<CodexPetDefinition> Discover();

    CodexPetDefinition? Find(string id);
}
