namespace Hearthcast.Data.Snapshots;

internal readonly record struct SnapshotPeriod(DateOnly Start, DateOnly End)
{
    public static SnapshotPeriod From(DateOnly date, DatePrecision precision)
    {
        if (!Enum.IsDefined(precision))
        {
            throw new ArgumentOutOfRangeException(nameof(precision));
        }

        return precision == DatePrecision.Month
            ? new SnapshotPeriod(new DateOnly(date.Year, date.Month, 1),
                new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)))
            : new SnapshotPeriod(date, date);
    }
}
