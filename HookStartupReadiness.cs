namespace Series4.Desktop;

public static class HookStartupReadiness
{
    public static async Task WaitAsync(
        Task readyTask,
        Task runTask,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(readyTask);
        ArgumentNullException.ThrowIfNull(runTask);

        var completedTask = await Task.WhenAny(readyTask, runTask)
            .WaitAsync(timeout, cancellationToken)
            .ConfigureAwait(false);

        if (
            ReferenceEquals(completedTask, readyTask)
            || readyTask.IsCompletedSuccessfully
        )
        {
            await readyTask.ConfigureAwait(false);
            return;
        }

        await runTask.ConfigureAwait(false);
        throw new InvalidOperationException(
            "The global hook exited before signaling readiness."
        );
    }
}
