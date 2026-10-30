using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hearthcast.Data.Tests.Infrastructure;

internal sealed class ConcurrentSaveBarrier : SaveChangesInterceptor
{
    private int _arrivals;
    private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Arrivals => Volatile.Read(ref _arrivals);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        // Both operations have completed their reads and domain validation before either writes.
        // Only gate the initial two attempts: a serializable retry must reload and proceed freely.
        var arrival = Interlocked.Increment(ref _arrivals);
        if (arrival == 2)
        {
            _bothReady.TrySetResult();
        }
        if (arrival <= 2)
        {
            await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}
