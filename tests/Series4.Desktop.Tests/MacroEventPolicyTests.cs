using SharpHook.Data;
using Xunit;

namespace Series4.Desktop.Tests;

public sealed class MacroEventPolicyTests
{
    [Fact]
    public void IncompleteProject_IsWarning_NotTechnicalBlock()
    {
        var recordedEvent = CreateValidTextEvent("업무 입력");

        var warning = GetWarnings(
            recordedEvent,
            MacroProjectStatus.InProgress
        );

        Assert.Contains("정상 종료 전 기록", warning);
        Assert.Null(MacroEventPolicy.GetTechnicalBlockReason(recordedEvent));
        Assert.True(recordedEvent.IsExecutable);
    }

    [Fact]
    public void EventAfterVideo_IsWarning_NotTechnicalBlock()
    {
        var recordedEvent = CreateValidTextEvent("업무 입력");
        recordedEvent.Offset = TimeSpan.FromSeconds(60);

        var warning = MacroEventPolicy.GetReviewWarningText(
            recordedEvent,
            MacroProjectStatus.Completed,
            TimeSpan.FromSeconds(53),
            0,
            0,
            1920,
            1080
        );

        Assert.Contains("영상 종료 후 실행", warning);
        Assert.Null(MacroEventPolicy.GetTechnicalBlockReason(recordedEvent));
    }

    [Fact]
    public void ChangedScreenBounds_IsWarning_NotTechnicalBlock()
    {
        var recordedEvent = CreatePointerEvent(300, 400);

        var warning = MacroEventPolicy.GetReviewWarningText(
            recordedEvent,
            MacroProjectStatus.Completed,
            TimeSpan.FromSeconds(10),
            0,
            0,
            2560,
            1440
        );

        Assert.Contains("화면 크기 또는 위치가 다릅니다", warning);
        Assert.Null(MacroEventPolicy.GetTechnicalBlockReason(recordedEvent));
    }

    [Fact]
    public void AltTab_IsWarning_NotTechnicalBlock()
    {
        var recordedEvent = CreateValidKeyEvent(
            KeyCode.VcLeftAlt,
            KeyCode.VcTab
        );

        var warning = GetWarnings(recordedEvent);

        Assert.Contains("창 전환 키", warning);
        Assert.Null(MacroEventPolicy.GetTechnicalBlockReason(recordedEvent));
    }

    [Theory]
    [InlineData("010-1234-5678", "전화번호")]
    [InlineData("person@example.go.kr", "이메일")]
    [InlineData("인증번호 123456", "민감 키워드")]
    [InlineData("123456789012", "긴 숫자열")]
    public void SensitiveText_IsWarning_NotTechnicalBlock(
        string text,
        string expectedKind
    )
    {
        var recordedEvent = CreateValidTextEvent(text);

        var warning = GetWarnings(recordedEvent);

        Assert.Contains(expectedKind, warning);
        Assert.Null(MacroEventPolicy.GetTechnicalBlockReason(recordedEvent));
    }

    [Fact]
    public void RuntimeCursorPointer_IsWarningAndExecutable()
    {
        var recordedEvent = CreatePointerEvent(null, null);

        recordedEvent.ReviewWarningText = GetWarnings(recordedEvent);

        Assert.Contains("현재 커서 위치", recordedEvent.ReviewWarningText);
        Assert.True(recordedEvent.IsExecutable);
    }

    [Fact]
    public void EmptyText_IsTechnicalBlock()
    {
        var recordedEvent = CreateValidTextEvent(string.Empty);

        var reason = MacroEventPolicy.GetTechnicalBlockReason(recordedEvent);

        Assert.Contains("비어", reason);
    }

    [Fact]
    public void PartialPointerCoordinates_AreTechnicalBlock()
    {
        var recordedEvent = CreatePointerEvent(100, null);

        var reason = MacroEventPolicy.GetTechnicalBlockReason(recordedEvent);

        Assert.Contains("X 또는 Y", reason);
    }

    [Fact]
    public void EmergencyStopChord_IsTechnicalBlock()
    {
        var recordedEvent = CreateValidKeyEvent(
            KeyCode.VcLeftControl,
            KeyCode.VcLeftShift,
            KeyCode.VcF12
        );

        var reason = MacroEventPolicy.GetTechnicalBlockReason(recordedEvent);

        Assert.Contains("긴급 중지", reason);
    }

    [Theory]
    [InlineData("영상 길이 10.000초 밖의 이벤트입니다.")]
    [InlineData("녹화가 정상 완료되지 않아 자동 실행에서 제외했습니다.")]
    [InlineData("녹화 앱 전환에 사용될 수 있는 창 전환 단축키라 격리했습니다.")]
    public void LegacySoftQuarantine_IsRecognized(string reason)
    {
        Assert.True(MacroEventPolicy.IsLegacyReviewOnlyQuarantine(reason));
    }

    private static string GetWarnings(
        RecordedEvent recordedEvent,
        MacroProjectStatus status = MacroProjectStatus.Completed
    )
    {
        return MacroEventPolicy.GetReviewWarningText(
                recordedEvent,
                status,
                TimeSpan.FromSeconds(120),
                0,
                0,
                1920,
                1080
            )
            ?? string.Empty;
    }

    private static RecordedEvent CreateValidTextEvent(string text)
    {
        var recordedEvent = CreateEvent(MacroActionKind.TextEntry);
        recordedEvent.ActionText = text;
        return recordedEvent;
    }

    private static RecordedEvent CreateValidKeyEvent(params KeyCode[] keys)
    {
        var recordedEvent = CreateEvent(MacroActionKind.KeyStroke);
        recordedEvent.KeyCodes = keys;
        return recordedEvent;
    }

    private static RecordedEvent CreatePointerEvent(double? x, double? y)
    {
        var recordedEvent = CreateEvent(MacroActionKind.MouseLeftClick);
        recordedEvent.ScreenX = x;
        recordedEvent.ScreenY = y;
        return recordedEvent;
    }

    private static RecordedEvent CreateEvent(MacroActionKind actionKind)
    {
        return new RecordedEvent
        {
            Offset = TimeSpan.FromSeconds(1),
            Category = "테스트",
            Message = "테스트 이벤트",
            ActionKind = actionKind,
            CaptureLeft = 0,
            CaptureTop = 0,
            CaptureWidth = 1920,
            CaptureHeight = 1080,
        };
    }
}
