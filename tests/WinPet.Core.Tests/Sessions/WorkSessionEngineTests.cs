using WinPet.Core.Activity;
using WinPet.Core.Sessions;

namespace WinPet.Core.Tests.Sessions;

public sealed class WorkSessionEngineTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 6, 18, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Active_use_moves_from_working_to_warning_and_break_due()
    {
        var engine = CreateEngine();

        engine.Process(Active(Start));
        var warning = engine.Process(Active(Start.AddMinutes(40)));
        var breakDue = engine.Process(Active(Start.AddMinutes(45)));

        Assert.Equal(WorkSessionState.Warning, warning.State);
        Assert.Equal(TimeSpan.FromMinutes(40), warning.ContinuousWorkDuration);
        Assert.Equal(WorkSessionState.BreakDue, breakDue.State);
    }

    [Fact]
    public void Short_idle_period_does_not_reset_continuous_work()
    {
        var engine = CreateEngine();

        engine.Process(Active(Start));
        engine.Process(Active(Start.AddMinutes(30)));
        var idle = engine.Process(
            Idle(Start.AddMinutes(32), TimeSpan.FromMinutes(2)));
        var resumed = engine.Process(Active(Start.AddMinutes(33)));

        Assert.Equal(WorkSessionState.Idle, idle.State);
        Assert.False(idle.QualifiedBreakCompleted);
        Assert.Equal(TimeSpan.FromMinutes(31), resumed.ContinuousWorkDuration);
    }

    [Fact]
    public void Qualified_break_resets_the_continuous_session()
    {
        var engine = CreateEngine();

        engine.Process(Active(Start));
        engine.Process(Active(Start.AddMinutes(30)));
        var resting = engine.Process(
            Idle(Start.AddMinutes(35), TimeSpan.FromMinutes(5)));
        var resumed = engine.Process(Active(Start.AddMinutes(36)));

        Assert.Equal(WorkSessionState.Resting, resting.State);
        Assert.True(resting.QualifiedBreakCompleted);
        Assert.Equal(TimeSpan.Zero, resting.ContinuousWorkDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), resumed.ContinuousWorkDuration);
    }

    [Fact]
    public void Locked_session_counts_toward_a_qualified_break()
    {
        var engine = CreateEngine();

        engine.Process(Active(Start));
        engine.Process(Active(Start.AddMinutes(20)));
        engine.Process(Locked(Start.AddMinutes(22)));
        var resting = engine.Process(Locked(Start.AddMinutes(25)));

        Assert.Equal(WorkSessionState.Resting, resting.State);
        Assert.True(resting.QualifiedBreakCompleted);
    }

    [Fact]
    public void Paused_time_is_not_counted()
    {
        var engine = CreateEngine();

        engine.Process(Active(Start));
        engine.Process(Active(Start.AddMinutes(10)));
        engine.SetPaused(true, Start.AddMinutes(10));
        var paused = engine.Process(Active(Start.AddMinutes(30)));
        engine.SetPaused(false, Start.AddMinutes(30));
        var resumed = engine.Process(Active(Start.AddMinutes(35)));

        Assert.Equal(WorkSessionState.Paused, paused.State);
        Assert.Equal(TimeSpan.FromMinutes(15), resumed.ContinuousWorkDuration);
    }

    private static WorkSessionEngine CreateEngine() =>
        new(new WorkSessionSettings());

    private static ActivitySnapshot Active(DateTimeOffset timestamp) =>
        new(timestamp, TimeSpan.Zero);

    private static ActivitySnapshot Idle(
        DateTimeOffset timestamp,
        TimeSpan idleDuration) =>
        new(timestamp, idleDuration);

    private static ActivitySnapshot Locked(DateTimeOffset timestamp) =>
        new(timestamp, TimeSpan.Zero, IsSessionLocked: true);
}
