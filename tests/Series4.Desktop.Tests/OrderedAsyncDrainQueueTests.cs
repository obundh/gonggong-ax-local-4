using Xunit;

namespace Series4.Desktop.Tests;

public sealed class OrderedAsyncDrainQueueTests
{
    [Fact]
    public async Task CompleteAndDrainAsync_PreservesOrderAndWaitsForDispatch()
    {
        var dispatched = new List<int>();
        var secondItemEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseSecondItem = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var queue = new OrderedAsyncDrainQueue<int>(async item =>
        {
            dispatched.Add(item);
            if (item == 2)
            {
                secondItemEntered.TrySetResult();
                await releaseSecondItem.Task;
            }
        });

        Assert.True(queue.TryEnqueue(1));
        Assert.True(queue.TryEnqueue(2));
        Assert.True(queue.TryEnqueue(3));

        var drainTask = queue.CompleteAndDrainAsync();
        await secondItemEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(drainTask.IsCompleted);

        releaseSecondItem.TrySetResult();
        await drainTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([1, 2, 3], dispatched);
    }

    [Fact]
    public async Task Complete_RejectsLaterItems()
    {
        var dispatched = new List<int>();
        var queue = new OrderedAsyncDrainQueue<int>(item =>
        {
            dispatched.Add(item);
            return ValueTask.CompletedTask;
        });

        Assert.True(queue.TryEnqueue(1));
        queue.Complete();

        Assert.False(queue.TryEnqueue(2));
        await queue.CompleteAndDrainAsync();
        Assert.Equal([1], dispatched);
    }
}
