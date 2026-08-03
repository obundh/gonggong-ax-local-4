using System.Threading.Channels;

namespace Series4.Desktop;

public sealed class OrderedAsyncDrainQueue<T>
{
    private readonly Channel<T> channel;
    private readonly Func<T, ValueTask> dispatchAsync;
    private readonly Task drainTask;
    private int completionRequested;

    public OrderedAsyncDrainQueue(Func<T, ValueTask> dispatchAsync)
    {
        ArgumentNullException.ThrowIfNull(dispatchAsync);
        this.dispatchAsync = dispatchAsync;
        channel = Channel.CreateUnbounded<T>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            }
        );
        drainTask = Task.Run(DrainAsync);
    }

    public bool TryEnqueue(T item)
    {
        return Volatile.Read(ref completionRequested) == 0
            && channel.Writer.TryWrite(item);
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref completionRequested, 1) == 0)
        {
            channel.Writer.TryComplete();
        }
    }

    public async Task CompleteAndDrainAsync()
    {
        Complete();
        await drainTask.ConfigureAwait(false);
    }

    private async Task DrainAsync()
    {
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync())
            {
                await dispatchAsync(item).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref completionRequested, 1);
            channel.Writer.TryComplete();
        }
    }
}
