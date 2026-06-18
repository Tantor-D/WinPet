using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WinPet.Desktop.Controls;

public sealed class CodexPetSprite : Control
{
    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<CodexPetSprite, string?>(nameof(SourcePath));

    public static readonly StyledProperty<int> AnimationRowProperty =
        AvaloniaProperty.Register<CodexPetSprite, int>(nameof(AnimationRow));

    private static readonly int[][] RowDurations =
    [
        [280, 110, 110, 140, 140, 320],
        [120, 120, 120, 120, 120, 120, 120, 220],
        [120, 120, 120, 120, 120, 120, 120, 220],
        [140, 140, 140, 280],
        [140, 140, 140, 140, 280],
        [140, 140, 140, 140, 140, 140, 140, 240],
        [150, 150, 150, 150, 150, 260],
        [120, 120, 120, 120, 120, 220],
        [150, 150, 150, 150, 150, 280],
    ];

    private readonly DispatcherTimer _timer;
    private Bitmap? _bitmap;
    private int _frame;
    private DateTimeOffset _frameStartedAt = DateTimeOffset.UtcNow;

    public CodexPetSprite()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30),
        };
        _timer.Tick += OnTick;
        _timer.Start();

        SourcePathProperty.Changed.AddClassHandler<CodexPetSprite>(
            (control, _) => control.LoadBitmap());
        AnimationRowProperty.Changed.AddClassHandler<CodexPetSprite>(
            (control, _) => control.ResetAnimation());
        AttachedToVisualTree += (_, _) =>
        {
            LoadBitmap();
            _timer.Start();
        };
        DetachedFromVisualTree += (_, _) => DisposeResources();
    }

    public string? SourcePath
    {
        get => GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public int AnimationRow
    {
        get => GetValue(AnimationRowProperty);
        set => SetValue(AnimationRowProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_bitmap is null || AnimationRow is < 0 or > 8)
        {
            return;
        }

        const double cellWidth = 192;
        const double cellHeight = 208;
        var source = new Rect(
            _frame * cellWidth,
            AnimationRow * cellHeight,
            cellWidth,
            cellHeight);
        var scale = Math.Min(
            Bounds.Width / cellWidth,
            Bounds.Height / cellHeight);
        var width = cellWidth * scale;
        var height = cellHeight * scale;
        var destination = new Rect(
            (Bounds.Width - width) / 2,
            Bounds.Height - height,
            width,
            height);
        context.DrawImage(_bitmap, source, destination);
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (AnimationRow is < 0 or > 8)
        {
            return;
        }

        var durations = RowDurations[AnimationRow];
        if (DateTimeOffset.UtcNow - _frameStartedAt <
            TimeSpan.FromMilliseconds(durations[_frame]))
        {
            return;
        }

        _frame = (_frame + 1) % durations.Length;
        _frameStartedAt = DateTimeOffset.UtcNow;
        InvalidateVisual();
    }

    private void LoadBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        if (!string.IsNullOrWhiteSpace(SourcePath) &&
            File.Exists(SourcePath))
        {
            _bitmap = new Bitmap(SourcePath);
        }

        ResetAnimation();
    }

    private void ResetAnimation()
    {
        _frame = 0;
        _frameStartedAt = DateTimeOffset.UtcNow;
        InvalidateVisual();
    }

    private void DisposeResources()
    {
        _timer.Stop();
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
