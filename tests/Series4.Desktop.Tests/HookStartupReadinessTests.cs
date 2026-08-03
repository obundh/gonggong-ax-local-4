using Xunit;

namespace Series4.Desktop.Tests;

public sealed class HookStartupReadinessTests
{
    [Fact]
    public async Task WaitAsync_ReturnsOnlyAfterReadinessIsSignaled()
    {
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var run = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        var waitTask = HookStartupReadiness.WaitAsync(
            ready.Task,
            run.Task,
            TimeSpan.FromSeconds(2)
        );
        Assert.False(waitTask.IsCompleted);

        ready.SetResult();
        await waitTask;
        Assert.False(run.Task.IsCompleted);
    }

    [Fact]
    public async Task WaitAsync_RejectsRunCompletionBeforeReadiness()
    {
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HookStartupReadiness.WaitAsync(
                ready.Task,
                Task.CompletedTask,
                TimeSpan.FromSeconds(2)
            )
        );

        Assert.Contains("before signaling readiness", exception.Message);
    }

    [Fact]
    public async Task WaitAsync_PropagatesCancellationDuringStartup()
    {
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var run = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HookStartupReadiness.WaitAsync(
                ready.Task,
                run.Task,
                TimeSpan.FromSeconds(2),
                cancellation.Token
            )
        );
    }
}
