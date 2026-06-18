namespace WinPet.Core.Platform;

public interface IAutoStartService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
