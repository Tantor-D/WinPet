using Avalonia.Controls;
using Avalonia.Input;

namespace WinPet.Desktop.Views;

public partial class PetWindow : Window
{
    public PetWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        DoubleTapped += (_, _) => BubbleToggled?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? BubbleToggled;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(args);
        }
    }
}
