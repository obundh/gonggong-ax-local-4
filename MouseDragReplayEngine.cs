using SharpHook.Data;

namespace Series4.Desktop;

public static class MouseDragReplayEngine
{
    public static void Execute(
        IReadOnlyList<MousePathPoint> path,
        MouseButton button,
        Action<int, int> moveCursor,
        Action<MouseButton> pressButton,
        Action<MouseButton> releaseButton,
        CancellationToken cancellationToken,
        Action<TimeSpan, CancellationToken>? wait = null
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(moveCursor);
        ArgumentNullException.ThrowIfNull(pressButton);
        ArgumentNullException.ThrowIfNull(releaseButton);

        if (path.Count < 2)
        {
            throw new ArgumentException(
                "드래그 경로에는 시작점과 끝점이 필요합니다.",
                nameof(path)
            );
        }
        if (
            button
                is not (
                    MouseButton.Button1
                    or MouseButton.Button2
                    or MouseButton.Button3
                )
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(button),
                $"지원하지 않는 드래그 버튼입니다: {button}"
            );
        }

        var orderedPath = path
            .OrderBy(point => point.Offset)
            .ToArray();
        var waitFor = wait ?? WaitWithCancellation;

        cancellationToken.ThrowIfCancellationRequested();
        moveCursor(orderedPath[0].X, orderedPath[0].Y);
        cancellationToken.ThrowIfCancellationRequested();

        Exception? actionError = null;
        Exception? releaseError = null;
        var pressed = false;
        var previousOffset = TimeSpan.Zero;
        try
        {
            pressButton(button);
            pressed = true;

            foreach (var point in orderedPath.Skip(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var delay = point.Offset - previousOffset;
                if (delay > TimeSpan.Zero)
                {
                    waitFor(delay, cancellationToken);
                }
                moveCursor(point.X, point.Y);
                previousOffset = point.Offset;
            }
        }
        catch (Exception exception)
        {
            actionError = exception;
        }
        finally
        {
            if (pressed)
            {
                try
                {
                    releaseButton(button);
                }
                catch (Exception exception)
                {
                    releaseError = exception;
                }
            }
        }

        if (actionError is not null && releaseError is not null)
        {
            throw new MouseButtonReleaseException(
                "드래그 실행과 마우스 버튼 해제에 모두 실패했습니다.",
                new AggregateException(actionError, releaseError)
            );
        }
        if (releaseError is not null)
        {
            throw new MouseButtonReleaseException(
                "드래그 후 마우스 버튼을 해제하지 못했습니다.",
                releaseError
            );
        }
        if (actionError is not null)
        {
            throw actionError;
        }
    }

    private static void WaitWithCancellation(
        TimeSpan duration,
        CancellationToken cancellationToken
    )
    {
        if (
            duration > TimeSpan.Zero
            && cancellationToken.WaitHandle.WaitOne(duration)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}

public sealed class MouseButtonReleaseException(
    string message,
    Exception innerException
) : Exception(message, innerException);
