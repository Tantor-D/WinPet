namespace WinPet.Core.History;

public interface IActivityDataManager
{
    Task<string> ExportCsvArchiveAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default);

    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
