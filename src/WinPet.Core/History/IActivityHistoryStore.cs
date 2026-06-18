using WinPet.Core.Activity;
using WinPet.Core.Sessions;

namespace WinPet.Core.History;

public interface IActivityHistoryStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task RecordAsync(
        ActivitySnapshot snapshot,
        WorkSessionUpdate session,
        CancellationToken cancellationToken = default);

    Task<DailyActivitySummary> GetDailySummaryAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default);
}
