using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateBudgetSnapshotRequest : IValidatableObject
{
    [Required] public Guid? Id { get; init; }
    [Required] public DateOnly? EffectiveFrom { get; init; }
    [Required] public IReadOnlyList<BudgetEntryRequest?>? Entries { get; init; }
    public Guid? CopiedFromBudgetSnapshotId { get; init; }
    public string? Notes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in BudgetSetupRequestValidation.Ids((Id, nameof(Id)), (CopiedFromBudgetSnapshotId, nameof(CopiedFromBudgetSnapshotId)))) yield return error;
        foreach (var error in BudgetSetupRequestValidation.Text((Notes, nameof(Notes)))) yield return error;
        foreach (var error in BudgetSnapshotRequestValidation.Date(EffectiveFrom, nameof(EffectiveFrom))) yield return error;
        if (Entries?.Any(e => e is null) == true)
            yield return new ValidationResult("Entries cannot contain null.", [nameof(Entries)]);
        var items = Entries?.Where(e => e?.BudgetItemId is not null).Select(e => e!.BudgetItemId!.Value).ToArray();
        if (items is not null && items.Distinct().Count() != items.Length)
            yield return new ValidationResult("Include a budget item only once per plan.", [nameof(Entries)]);
        if (Id.HasValue && (CopiedFromBudgetSnapshotId == Id || Entries?.Any(e => e?.Source?.SnapshotId == Id) == true))
            yield return new ValidationResult("A plan cannot use itself as a source.", [nameof(CopiedFromBudgetSnapshotId), nameof(Entries)]);
    }

    internal BudgetSnapshotValues ToValues() => new(EffectiveFrom!.Value, Entries!.Select(e => e!.ToValues()).ToArray(), CopiedFromBudgetSnapshotId, Notes);
}
