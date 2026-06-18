using WinPet.Core.Activity;

namespace WinPet.Core.Sessions;

public sealed class WorkSessionEngine
{
    private readonly WorkSessionSettings _settings;
    private DateTimeOffset? _lastTimestamp;
    private TimeSpan _continuousWorkDuration;
    private TimeSpan _activeDuration;
    private TimeSpan _currentBreakDuration;
    private bool _breakWasQualified;
    private bool _isPaused;

    public WorkSessionEngine(WorkSessionSettings settings)
    {
        settings.Validate();
        _settings = settings;
    }

    public WorkSessionUpdate Process(ActivitySnapshot snapshot)
    {
        if (_lastTimestamp is null)
        {
            _lastTimestamp = snapshot.Timestamp;
            return CreateUpdate(snapshot.Timestamp, DetermineState(snapshot), false);
        }

        var elapsed = snapshot.Timestamp - _lastTimestamp.Value;
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Activity snapshots must be ordered by timestamp.",
                nameof(snapshot));
        }

        _lastTimestamp = snapshot.Timestamp;

        if (_isPaused)
        {
            return CreateUpdate(snapshot.Timestamp, WorkSessionState.Paused, false);
        }

        var isActive = IsActive(snapshot);
        var qualifiedBreakCompleted = false;

        if (isActive)
        {
            _continuousWorkDuration += elapsed;
            _activeDuration += elapsed;
            _currentBreakDuration = TimeSpan.Zero;
            _breakWasQualified = false;
        }
        else
        {
            var observedBreak = snapshot.IsSessionLocked || snapshot.IsSystemSuspended
                ? _currentBreakDuration + elapsed
                : Max(_currentBreakDuration + elapsed, snapshot.IdleDuration);

            _currentBreakDuration = observedBreak;

            if (!_breakWasQualified &&
                _currentBreakDuration >= _settings.QualifiedBreakDuration)
            {
                _continuousWorkDuration = TimeSpan.Zero;
                _activeDuration = TimeSpan.Zero;
                _breakWasQualified = true;
                qualifiedBreakCompleted = true;
            }
        }

        return CreateUpdate(
            snapshot.Timestamp,
            DetermineState(snapshot),
            qualifiedBreakCompleted);
    }

    public WorkSessionUpdate Reset(DateTimeOffset timestamp)
    {
        _lastTimestamp = timestamp;
        _continuousWorkDuration = TimeSpan.Zero;
        _activeDuration = TimeSpan.Zero;
        _currentBreakDuration = TimeSpan.Zero;
        _breakWasQualified = false;

        return CreateUpdate(timestamp, WorkSessionState.Working, false);
    }

    public WorkSessionUpdate SetPaused(bool paused, DateTimeOffset timestamp)
    {
        _isPaused = paused;
        _lastTimestamp = timestamp;

        return CreateUpdate(
            timestamp,
            paused ? WorkSessionState.Paused : WorkSessionState.Working,
            false);
    }

    private WorkSessionState DetermineState(ActivitySnapshot snapshot)
    {
        if (_isPaused)
        {
            return WorkSessionState.Paused;
        }

        if (!IsActive(snapshot))
        {
            return _currentBreakDuration >= _settings.QualifiedBreakDuration
                ? WorkSessionState.Resting
                : WorkSessionState.Idle;
        }

        if (_continuousWorkDuration >= _settings.MaximumWorkDuration)
        {
            return WorkSessionState.BreakDue;
        }

        if (_continuousWorkDuration >=
            _settings.MaximumWorkDuration - _settings.WarningLeadTime)
        {
            return WorkSessionState.Warning;
        }

        return WorkSessionState.Working;
    }

    private bool IsActive(ActivitySnapshot snapshot) =>
        !snapshot.IsSessionLocked &&
        !snapshot.IsSystemSuspended &&
        snapshot.IdleDuration <= _settings.ActiveInputWindow;

    private WorkSessionUpdate CreateUpdate(
        DateTimeOffset timestamp,
        WorkSessionState state,
        bool qualifiedBreakCompleted)
    {
        var overtime = _continuousWorkDuration > _settings.MaximumWorkDuration
            ? _continuousWorkDuration - _settings.MaximumWorkDuration
            : TimeSpan.Zero;

        return new WorkSessionUpdate(
            timestamp,
            state,
            _continuousWorkDuration,
            _activeDuration,
            _currentBreakDuration,
            overtime,
            qualifiedBreakCompleted);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;
}
