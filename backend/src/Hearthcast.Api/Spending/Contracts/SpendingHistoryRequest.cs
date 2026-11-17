using System.ComponentModel.DataAnnotations;
using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingHistoryRequest : IValidatableObject
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public bool IncludeVoided { get; init; }
    [Range(1, SpendingRecordService.MaximumHistoryPageSize)] public int PageSize { get; init; } = SpendingRecordService.DefaultHistoryPageSize;
    [StringLength(4096)] public string? Cursor { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From > To) yield return new ValidationResult("Start date must not follow end date.", [nameof(From), nameof(To)]);
    }
}
