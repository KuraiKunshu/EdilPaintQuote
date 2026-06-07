using System.Diagnostics;
using System.Threading;

namespace EdilPaintPreventibiviGen.Services;

public static class AppShutdownManager
{
    private sealed record TrackedOperation(string Name, Task Task, CancellationTokenSource Cancellation);

    private static readonly object SyncRoot = new();
    private static readonly List<TrackedOperation> Operations = new();
    private static readonly CancellationTokenSource ShutdownCts = new();
    private static int _killSwitchArmed;

    public static CancellationToken ShutdownToken => ShutdownCts.Token;
    public static bool IsShutdownRequested { get; private set; }

    public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancellationToken = default)
    {
        return cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ShutdownToken, cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(ShutdownToken);
    }

    public static Task Track(string name, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        var linkedCts = CreateLinkedTokenSource(cancellationToken);
        Task task = Task.Run(async () =>
        {
            try
            {
                await operation(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[ShutdownManager] Operazione annullata: {name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShutdownManager] Operazione fallita ({name}): {ex.Message}");
            }
            finally
            {
                linkedCts.Dispose();
            }
        });

        Register(name, task, linkedCts);
        return task;
    }

    public static void Register(string name, Task task, CancellationTokenSource cancellation)
    {
        var operation = new TrackedOperation(name, task, cancellation);
        lock (SyncRoot)
        {
            Operations.Add(operation);
        }

        _ = task.ContinueWith(
            _ => Unregister(operation),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static void RequestShutdown()
    {
        IsShutdownRequested = true;
        try
        {
            ShutdownCts.Cancel();
        }
        catch
        {
        }

        foreach (var operation in Snapshot())
        {
            try
            {
                operation.Cancellation.Cancel();
            }
            catch
            {
            }
        }
    }

    public static async Task WaitForCompletionAsync(TimeSpan timeout)
    {
        var tasks = Snapshot()
            .Where(operation => !operation.Task.IsCompleted)
            .Select(operation => operation.Task)
            .ToArray();

        if (tasks.Length == 0)
            return;

        var allTasks = Task.WhenAll(tasks);
        Task completed = await Task.WhenAny(allTasks, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed == allTasks)
        {
            await allTasks.ConfigureAwait(false);
            return;
        }

        string active = string.Join(", ", Snapshot()
            .Where(operation => !operation.Task.IsCompleted)
            .Select(operation => operation.Name));
        Debug.WriteLine($"[ShutdownManager] Operazioni ancora attive dopo {timeout.TotalSeconds:F0}s: {active}");
    }

    public static void ArmProcessKillSwitch(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _killSwitchArmed, 1) == 1)
            return;

        var thread = new Thread(() =>
        {
            try
            {
                Thread.Sleep(timeout);
                Debug.WriteLine($"[ShutdownManager] Kill-switch attivato dopo {timeout.TotalSeconds:F0}s.");
                Process.GetCurrentProcess().Kill(entireProcessTree: true);
            }
            catch
            {
                try
                {
                    Environment.Exit(0);
                }
                catch
                {
                }
            }
        })
        {
            IsBackground = true,
            Name = "EdilPaint shutdown kill-switch"
        };

        thread.Start();
    }

    private static TrackedOperation[] Snapshot()
    {
        lock (SyncRoot)
            return Operations.ToArray();
    }

    private static void Unregister(TrackedOperation operation)
    {
        lock (SyncRoot)
        {
            Operations.Remove(operation);
        }
    }
}
