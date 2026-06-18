using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WinPet.Core.Pets;
using WinPet.Core.Sessions;
using WinPet.Desktop.Services;

namespace WinPet.Desktop.ViewModels;

public partial class PetWindowViewModel : ViewModelBase
{
    private WorkSessionState? _lastState;
    private CancellationTokenSource? _welcomeAnimation;
    [ObservableProperty]
    private string? _spritesheetPath;

    [ObservableProperty]
    private int _animationRow;

    [ObservableProperty]
    private string _message = "今天也慢慢来。";

    [ObservableProperty]
    private bool _isBubbleVisible = true;

    public PetWindowViewModel(
        ActivityTrackingService trackingService,
        CodexPetDefinition? pet)
    {
        SpritesheetPath = pet?.SpritesheetFullPath;
        trackingService.Updated += OnUpdated;
    }

    public void ApplyPet(CodexPetDefinition? pet)
    {
        SpritesheetPath = pet?.SpritesheetFullPath;
    }

    private void OnUpdated(object? sender, WorkSessionUpdate update) =>
        Dispatcher.UIThread.Post(() => Apply(update));

    private void Apply(WorkSessionUpdate update)
    {
        if (_lastState == WorkSessionState.Resting &&
            update.State == WorkSessionState.Working)
        {
            ShowWelcomeBack();
            _lastState = update.State;
            return;
        }

        _lastState = update.State;
        (AnimationRow, Message) = update.State switch
        {
            WorkSessionState.Working =>
                (7, "我在这儿陪你工作。"),
            WorkSessionState.Idle =>
                (0, "短暂放空也很好。"),
            WorkSessionState.Warning =>
                (6, "快到休息时间啦。"),
            WorkSessionState.BreakDue =>
                (5, "起来走走吧！"),
            WorkSessionState.Resting
                when update.QualifiedBreakCompleted =>
                (3, "休息达标，做得好。"),
            WorkSessionState.Resting =>
                (0, "好好休息，我替你守着。"),
            WorkSessionState.Paused =>
                (6, "计时暂停中。"),
            _ => (0, Message),
        };
    }

    private void ShowWelcomeBack()
    {
        _welcomeAnimation?.Cancel();
        _welcomeAnimation = new CancellationTokenSource();
        var token = _welcomeAnimation.Token;
        AnimationRow = 3;
        Message = "欢迎回来，开始新一轮吧。";
        _ = ReturnToWorkingAsync(token);
    }

    private async Task ReturnToWorkingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AnimationRow = 7;
                Message = "我在这儿陪你工作。";
            });
        }
        catch (OperationCanceledException)
        {
        }
    }
}
