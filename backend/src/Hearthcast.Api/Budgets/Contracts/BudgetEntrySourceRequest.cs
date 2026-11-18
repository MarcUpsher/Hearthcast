using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BudgetEntrySourceRequest : IValidatableObject
{
    [Required] public Guid? SnapshotId { get; init; }
    [Required] public Guid? EntryId { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BudgetSetupRequestValidation.Ids((SnapshotId, nameof(SnapshotId)), (EntryId, nameof(EntryId)));
    internal BudgetEntrySource ToValues() => new(SnapshotId!.Value, EntryId!.Value);
}
