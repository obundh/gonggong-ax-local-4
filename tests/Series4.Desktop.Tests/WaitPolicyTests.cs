using Xunit;

namespace Series4.Desktop.Tests;

public sealed class WaitPolicyTests
{
    [Theory]
    [InlineData("0.1", true)]
    [InlineData("1.5", true)]
    [InlineData("3600", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("0.09", false)]
    [InlineData("3600.1", false)]
    [InlineData("NaN", false)]
    [InlineData("Infinity", false)]
    [InlineData("", false)]
    [InlineData("1,5", false)]
    public void WaitDuration_EnforcesRangeAndFiniteValue(string text, bool valid)
    {
        Assert.Equal(valid, MacroEventPolicy.TryGetWaitSeconds(text, out _));
        var item = new RecordedEvent
        {
            ActionKind = MacroActionKind.Wait,
            ActionText = text,
            Offset = TimeSpan.Zero,
            Category = "대기",
            Message = "대기 테스트",
        };
        Assert.Equal(valid, MacroEventPolicy.GetTechnicalBlockReason(item) is null);
    }
}
