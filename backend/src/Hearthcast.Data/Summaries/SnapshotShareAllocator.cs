using System.Numerics;
using Hearthcast.Data.Summaries.Models;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Summaries;

// Allocate decimal units with largest remainders, retaining exact reconciliation without cent rounding.
internal static class SnapshotShareAllocator
{
    public static IReadOnlyDictionary<Guid, decimal?> Allocate(decimal? amount, IReadOnlyList<SnapshotPersonShare> allocations,
        IReadOnlyDictionary<Guid, Guid> originalPersonIds)
    {
        if (!amount.HasValue)
        {
            return allocations.ToDictionary(owner => owner.SnapshotPersonId, _ => (decimal?)null);
        }

        // Work with the exact integer coefficient to avoid intermediate decimal multiplication overflow.
        var bits = decimal.GetBits(amount.Value);
        var coefficient = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        var originalScale = (bits[3] >> 16) & 0xff;
        // Basis points need up to four extra places. Keep a common scale where the whole value fits,
        // so every partial sum of its same-sign shares is also representable without rounding.
        var scale = Math.Min(28, originalScale + 4);
        var maximumCoefficient = (BigInteger.One << 96) - 1;
        while (coefficient * BigInteger.Pow(10, scale - originalScale) > maximumCoefficient)
        {
            scale--;
        }
        var totalUnits = coefficient * BigInteger.Pow(10, scale - originalScale);
        var shares = allocations.Select(owner =>
        {
            var units = BigInteger.DivRem(totalUnits * owner.ShareBasisPoints,
                AllocationShareRules.FullShareBasisPoints, out var remainder);
            return (owner.SnapshotPersonId, Units: units, Remainder: remainder);
        }).OrderByDescending(share => share.Remainder)
            .ThenBy(share => originalPersonIds[share.SnapshotPersonId]).ToArray();
        var remaining = (int)(totalUnits - shares.Aggregate(BigInteger.Zero, (sum, share) => sum + share.Units));
        var negative = amount < 0;
        return shares.Select((share, index) =>
        {
            var units = share.Units + (index < remaining ? BigInteger.One : BigInteger.Zero);
            var attributedAmount = new decimal((int)(uint)(units & uint.MaxValue), (int)(uint)((units >> 32) & uint.MaxValue),
                (int)(uint)((units >> 64) & uint.MaxValue), negative, (byte)scale);
            return (share.SnapshotPersonId, Amount: (decimal?)attributedAmount);
        }).ToDictionary(share => share.SnapshotPersonId, share => share.Amount);
    }
}
