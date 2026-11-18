using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BudgetEntryRequest : IValidatableObject
{
    [Required] public Guid? BudgetItemId { get; init; }
    [Required, JsonConverter(typeof(BudgetEntryValueModeJsonConverter))] public BudgetEntryValueMode? Mode { get; init; }
    [JsonNumberHandling(JsonNumberHandling.Strict)] public decimal? PlannedAmount { get; init; }
    public DateOnly? ConfirmedOn { get; init; }
    public BudgetEntrySourceRequest? Source { get; init; }
    public bool RefreshFromSetup { get; init; }
    public BudgetEntryMetadataRequest? Metadata { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in BudgetSetupRequestValidation.Ids((BudgetItemId, nameof(BudgetItemId)))) yield return error;
        foreach (var error in BudgetSnapshotRequestValidation.Date(ConfirmedOn, nameof(ConfirmedOn))) yield return error;
        foreach (var error in BudgetSnapshotRequestValidation.Amount(PlannedAmount, nameof(PlannedAmount))) yield return error;
        if (Mode == BudgetEntryValueMode.Confirm && (!PlannedAmount.HasValue || !ConfirmedOn.HasValue))
            yield return new ValidationResult("Confirm requires an explicit amount and confirmation date.", [nameof(PlannedAmount), nameof(ConfirmedOn)]);
        if ((Mode is BudgetEntryValueMode.Carry or BudgetEntryValueMode.Unrecorded) && (PlannedAmount.HasValue || ConfirmedOn.HasValue))
            yield return new ValidationResult("Only Confirm accepts an entered amount/date.", [nameof(PlannedAmount), nameof(ConfirmedOn)]);
        if (RefreshFromSetup && Metadata is not null)
            yield return new ValidationResult("Select current setup or historical metadata, not both.", [nameof(RefreshFromSetup), nameof(Metadata)]);
    }

    internal BudgetEntryValues ToValues() => new(BudgetItemId!.Value, Mode!.Value, PlannedAmount, ConfirmedOn,
        Source?.ToValues(), RefreshFromSetup, Metadata?.ToValues());
}
