namespace WinPet.Core.Platform;

public interface ISystemNotificationService : IDisposable
{
    bool IsAvailable { get; }

    void Show(string title, string message);
}
