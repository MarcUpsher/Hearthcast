using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Persistence;

internal static class SerializableTransaction
{
    public static async Task<T> ExecuteAsync<T>(
        IDbContextFactory<HearthcastDbContext> contextFactory,
        Func<HearthcastDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        string? retryUniqueConstraint = null)
    {
        const int maximumAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // A retry must reload everything, including state changed by the failed attempt.
                await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                var result = await operation(context, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception) when (attempt < maximumAttempts &&
                (IsAbortedTransaction(exception) || IsRetryableUniqueViolation(exception, retryUniqueConstraint)))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsRetryableUniqueViolation(Exception exception, string? constraint) =>
        constraint is not null && exception.GetBaseException() is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation } postgres && postgres.ConstraintName == constraint;

    private static bool IsAbortedTransaction(Exception exception)
    {
        // These failures guarantee rollback. Do not retry an ambiguous network/commit failure.
        return exception.GetBaseException() is PostgresException
        {
            SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected
        };
    }
}
