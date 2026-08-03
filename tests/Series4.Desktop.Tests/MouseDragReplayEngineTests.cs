using SharpHook.Data;
using Xunit;

namespace Series4.Desktop.Tests;

public sealed class MouseDragReplayEngineTests
{
    [Fact]
    public void Execute_ReplaysEveryCapturedPointAndReleasesButton()
    {
        var path = new[]
        {
            new MousePathPoint(TimeSpan.Zero, 10, 20),
            new MousePathPoint(TimeSpan.FromMilliseconds(15), 30, 40),
            new MousePathPoint(TimeSpan.FromMilliseconds(35), 50, 25),
        };
        var actions = new List<string>();

        MouseDragReplayEngine.Execute(
            path,
            MouseButton.Button1,
            (x, y) => actions.Add($"move:{x},{y}"),
            button => actions.Add($"down:{button}"),
            button => actions.Add($"up:{button}"),
            CancellationToken.None,
            (_, _) => { }
        );

        Assert.Equal(
            [
                "move:10,20",
                "down:Button1",
                "move:30,40",
                "move:50,25",
                "up:Button1",
            ],
            actions
        );
    }

    [Fact]
    public void Execute_CancellationAfterInitialMove_DoesNotPressButton()
    {
        using var cancellation = new CancellationTokenSource();
        var pressed = false;
        var released = false;

        Assert.Throws<OperationCanceledException>(() =>
            MouseDragReplayEngine.Execute(
                [
                    new MousePathPoint(TimeSpan.Zero, 10, 20),
                    new MousePathPoint(TimeSpan.FromMilliseconds(10), 30, 40),
                ],
                MouseButton.Button1,
                (_, _) =>
                {
                    cancellation.Cancel();
                },
                _ => pressed = true,
                _ => released = true,
                cancellation.Token,
                (_, token) => token.ThrowIfCancellationRequested()
            )
        );

        Assert.False(pressed);
        Assert.False(released);
    }

    [Fact]
    public void Execute_CancellationAfterPress_StillReleasesButton()
    {
        using var cancellation = new CancellationTokenSource();
        var pressed = false;
        var released = false;

        Assert.Throws<OperationCanceledException>(() =>
            MouseDragReplayEngine.Execute(
                [
                    new MousePathPoint(TimeSpan.Zero, 10, 20),
                    new MousePathPoint(TimeSpan.FromMilliseconds(10), 30, 40),
                ],
                MouseButton.Button1,
                (_, _) => { },
                _ => pressed = true,
                _ => released = true,
                cancellation.Token,
                (_, token) =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
            )
        );

        Assert.True(pressed);
        Assert.True(released);
    }

    [Fact]
    public void Execute_ReleaseFailure_IsReportedAsCleanupFailure()
    {
        var exception = Assert.Throws<MouseButtonReleaseException>(() =>
            MouseDragReplayEngine.Execute(
                [
                    new MousePathPoint(TimeSpan.Zero, 10, 20),
                    new MousePathPoint(TimeSpan.FromMilliseconds(10), 30, 40),
                ],
                MouseButton.Button1,
                (_, _) => { },
                _ => { },
                _ => throw new InvalidOperationException("release failed"),
                CancellationToken.None,
                (_, _) => { }
            )
        );

        Assert.Contains("해제", exception.Message);
    }
}
