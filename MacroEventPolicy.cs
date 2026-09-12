using System.Text.RegularExpressions;
using SharpHook.Data;

namespace Series4.Desktop;

public static class MacroEventPolicy
{
    private static readonly Regex ResidentNumberPattern = new(
        @"(?<!\d)\d{6}[- ]?\d{7}(?!\d)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100)
    );

    private static readonly Regex PhoneNumberPattern = new(
        @"(?<!\d)01[016789][-. ]?\d{3,4}[-. ]?\d{4}(?!\d)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100)
    );

    private static readonly Regex EmailPattern = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100)
    );

    private static readonly Regex LongNumberPattern = new(
        @"(?<!\d)\d{10,19}(?!\d)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100)
    );

    private static readonly Regex SensitiveKeywordPattern = new(
        @"비밀번호|패스워드|인증번호|OTP|계좌|주민등록|주민번호",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100)
    );

    public static string? GetTechnicalBlockReason(RecordedEvent recordedEvent)
    {
        ArgumentNullException.ThrowIfNull(recordedEvent);

        var invalidPayloadReason = recordedEvent.ActionKind switch
        {
            MacroActionKind.Wait when !TryGetWaitSeconds(recordedEvent.ActionText, out _) =>
                "대기 시간은 0.1~3600초여야 합니다.",
            MacroActionKind.TextEntry
                when string.IsNullOrEmpty(recordedEvent.ActionText) =>
                "입력할 텍스트가 비어 있습니다.",
            MacroActionKind.KeyStroke when recordedEvent.KeyCodes.Length == 0 =>
                "실행할 키 정보가 비어 있습니다.",
            MacroActionKind.MouseWheel when recordedEvent.WheelRotation == 0 =>
                "휠 이동량이 0입니다.",
            MacroActionKind.MouseDrag
                when recordedEvent.DragButton is null =>
                "드래그 버튼 정보가 없습니다.",
            MacroActionKind.MouseDrag
                when recordedEvent.MousePath.Length < 2 =>
                "드래그 경로가 없습니다.",
            _ => null,
        };
        if (invalidPayloadReason is not null)
        {
            return invalidPayloadReason;
        }

        if (
            IsPointerAction(recordedEvent.ActionKind)
            && recordedEvent.ScreenX.HasValue != recordedEvent.ScreenY.HasValue
        )
        {
            return "마우스 좌표의 X 또는 Y 값이 누락되었습니다.";
        }

        if (
            recordedEvent.ActionKind == MacroActionKind.KeyStroke
            && IsEmergencyStopKeyChord(recordedEvent.KeyCodes)
        )
        {
            return "긴급 중지 전용 단축키는 실행 항목으로 사용할 수 없습니다.";
        }

        if (
            IsPointerAction(recordedEvent.ActionKind)
            && recordedEvent.ScreenX is double x
            && recordedEvent.ScreenY is double y
            && (
                !double.IsFinite(x)
                || !double.IsFinite(y)
                || recordedEvent.CaptureWidth <= 0
                || recordedEvent.CaptureHeight <= 0
                || x < recordedEvent.CaptureLeft
                || x >= recordedEvent.CaptureLeft + recordedEvent.CaptureWidth
                || y < recordedEvent.CaptureTop
                || y >= recordedEvent.CaptureTop + recordedEvent.CaptureHeight
            )
        )
        {
            return "녹화된 화면 밖의 마우스 이벤트입니다.";
        }

        return null;
    }

    public static string? GetReviewWarningText(
        RecordedEvent recordedEvent,
        MacroProjectStatus projectStatus,
        TimeSpan? videoDuration,
        int currentCaptureLeft,
        int currentCaptureTop,
        int currentCaptureWidth,
        int currentCaptureHeight
    )
    {
        ArgumentNullException.ThrowIfNull(recordedEvent);
        var warnings = new List<string>();

        if (
            projectStatus != MacroProjectStatus.Completed
            && recordedEvent.ActionKind != MacroActionKind.None
        )
        {
            warnings.Add(
                "정상 종료 전 기록입니다. 영상과 이벤트 시점이 어긋날 수 있습니다."
            );
        }

        if (
            videoDuration.HasValue
            && recordedEvent.ActionKind != MacroActionKind.None
            && recordedEvent.Offset > videoDuration.Value
        )
        {
            warnings.Add("영상 종료 후 실행되는 이벤트입니다.");
        }

        if (
            IsPointerAction(recordedEvent.ActionKind)
            && recordedEvent.ScreenX.HasValue
            && recordedEvent.ScreenY.HasValue
            && (
                recordedEvent.CaptureLeft != currentCaptureLeft
                || recordedEvent.CaptureTop != currentCaptureTop
                || recordedEvent.CaptureWidth != currentCaptureWidth
                || recordedEvent.CaptureHeight != currentCaptureHeight
            )
        )
        {
            warnings.Add(
                "녹화 때와 화면 크기 또는 위치가 다릅니다. 기존 절대 좌표로 실행됩니다."
            );
        }

        if (
            recordedEvent.ActionKind == MacroActionKind.KeyStroke
            && IsWindowNavigationChord(recordedEvent.KeyCodes)
        )
        {
            warnings.Add(
                "창 전환 키가 포함되어 있습니다. 이후 입력 대상 창을 확인하세요."
            );
        }

        if (
            IsPointerAction(recordedEvent.ActionKind)
            && !recordedEvent.ScreenX.HasValue
            && !recordedEvent.ScreenY.HasValue
        )
        {
            warnings.Add("실행 시점의 현재 커서 위치를 사용합니다.");
        }

        var sensitiveKind = GetSensitiveTextKind(recordedEvent.ActionText);
        if (sensitiveKind is not null)
        {
            warnings.Add($"민감정보 형식 의심 · {sensitiveKind}");
        }

        return warnings.Count == 0 ? null : string.Join(" · ", warnings);
    }

    public static bool IsLegacyReviewOnlyQuarantine(string? reason)
    {
        return reason?.StartsWith("영상 길이 ", StringComparison.Ordinal) == true
            || reason?.StartsWith(
                "녹화가 정상 완료되지 않아",
                StringComparison.Ordinal
            ) == true
            || reason?.StartsWith(
                "녹화 앱 전환에 사용될 수 있는",
                StringComparison.Ordinal
            ) == true;
    }

    public static bool TryGetWaitSeconds(string? text, out double seconds) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds)
        && double.IsFinite(seconds) && seconds >= 0.1 && seconds <= 3600;

    public static bool IsPointerAction(MacroActionKind actionKind)
    {
        return actionKind
            is MacroActionKind.MouseLeftClick
                or MacroActionKind.MouseRightClick
                or MacroActionKind.MouseMiddleClick
                or MacroActionKind.MouseDrag
                or MacroActionKind.MouseWheel;
    }

    private static bool IsWindowNavigationChord(IEnumerable<KeyCode> keyCodes)
    {
        var keys = keyCodes.ToHashSet();
        return keys.Contains(KeyCode.VcTab)
            && keys.Any(
                key =>
                    key
                        is KeyCode.VcLeftAlt
                            or KeyCode.VcRightAlt
                            or KeyCode.VcLeftMeta
                            or KeyCode.VcRightMeta
            );
    }

    private static bool IsEmergencyStopKeyChord(IEnumerable<KeyCode> keyCodes)
    {
        var keys = keyCodes.ToHashSet();
        return keys.Contains(KeyCode.VcF12)
            && (
                keys.Contains(KeyCode.VcLeftControl)
                || keys.Contains(KeyCode.VcRightControl)
            )
            && (
                keys.Contains(KeyCode.VcLeftShift)
                || keys.Contains(KeyCode.VcRightShift)
            );
    }

    private static string? GetSensitiveTextKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (ResidentNumberPattern.IsMatch(text))
        {
            return "주민등록번호 형태";
        }
        if (PhoneNumberPattern.IsMatch(text))
        {
            return "전화번호 형태";
        }
        if (EmailPattern.IsMatch(text))
        {
            return "이메일 주소";
        }
        if (LongNumberPattern.IsMatch(text))
        {
            return "긴 숫자열";
        }
        return SensitiveKeywordPattern.IsMatch(text)
            ? "민감 키워드"
            : null;
    }
}
