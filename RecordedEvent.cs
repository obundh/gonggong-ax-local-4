using System.ComponentModel;
using System.Runtime.CompilerServices;
using SharpHook.Data;

namespace Series4.Desktop;

public sealed class RecordedEvent : INotifyPropertyChanged
{
    private TimeSpan offset;
    private string category = string.Empty;
    private string message = string.Empty;
    private string? overlayLabel;
    private MacroActionKind actionKind;
    private bool isQuarantined;
    private string? quarantineReason;

    public required TimeSpan Offset
    {
        get => offset;
        set
        {
            if (offset == value)
            {
                return;
            }

            offset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimeText));
        }
    }

    public required string Category
    {
        get => category;
        set
        {
            if (category == value)
            {
                return;
            }

            category = value;
            OnPropertyChanged();
        }
    }

    public required string Message
    {
        get => message;
        set
        {
            if (message == value)
            {
                return;
            }

            message = value;
            OnPropertyChanged();
        }
    }

    public string? OverlayLabel
    {
        get => overlayLabel;
        set
        {
            if (overlayLabel == value)
            {
                return;
            }

            overlayLabel = value;
            OnPropertyChanged();
        }
    }

    public MacroActionKind ActionKind
    {
        get => actionKind;
        set
        {
            if (actionKind == value)
            {
                return;
            }

            actionKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsExecutable));
        }
    }

    public string? ActionText { get; set; }

    public KeyCode[] KeyCodes { get; set; } = [];

    public int WheelRotation { get; set; }

    public bool IsHorizontalWheel { get; set; }

    public KeyCode[] ModifierKeyCodes { get; set; } = [];

    public bool IsQuarantined
    {
        get => isQuarantined;
        set
        {
            if (isQuarantined == value)
            {
                return;
            }

            isQuarantined = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsExecutable));
        }
    }

    public string? QuarantineReason
    {
        get => quarantineReason;
        set
        {
            if (quarantineReason == value)
            {
                return;
            }

            quarantineReason = value;
            OnPropertyChanged();
        }
    }

    public long Sequence { get; set; }

    public bool IsExecutable =>
        ActionKind != MacroActionKind.None && !IsQuarantined;

    public double? ScreenX { get; set; }

    public double? ScreenY { get; set; }

    public int CaptureLeft { get; set; }

    public int CaptureTop { get; set; }

    public int CaptureWidth { get; set; }

    public int CaptureHeight { get; set; }

    public string TimeText
    {
        get
        {
            var rounded = TimeSpan.FromMilliseconds(
                Math.Round(Offset.TotalMilliseconds, MidpointRounding.AwayFromZero)
            );
            return $"{(int)rounded.TotalMinutes:00}:{rounded.Seconds:00}.{rounded.Milliseconds:000}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
