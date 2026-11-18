using Hearthcast.Api.Budgets.Contracts;
using Hearthcast.Data.Budgets;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Budgets;

[ApiController]
[Route("api/households/{householdId:guid}/budgets/snapshots")]
public sealed class BudgetSnapshotsController(BudgetSnapshotService snapshots, TimeProvider timeProvider) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BudgetSnapshotResponse>> Create(Guid householdId, CreateBudgetSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var result = await snapshots.CreateAsync(householdId, request.Id!.Value, request.ToValues(), cancellationToken);
        var response = BudgetSnapshotResponse.From(result.Snapshot);
        return result.WasCreated ? CreatedAtAction(nameof(Get), new { householdId, snapshotId = response.Id }, response) : Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<BudgetHistoryPageResponse>> List(Guid householdId, [FromQuery] BudgetHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var page = await snapshots.ListAsync(householdId, request.Before, request.PageSize, cancellationToken);
        return new BudgetHistoryPageResponse(page.Records.Select(BudgetSnapshotResponse.From).ToArray(), page.NextBefore, page.PageSize);
    }

    [HttpGet("{snapshotId:guid}")]
    public async Task<ActionResult<BudgetSnapshotResponse>> Get(Guid householdId, Guid snapshotId, CancellationToken cancellationToken) =>
        BudgetSnapshotResponse.From(await snapshots.GetAsync(householdId, snapshotId, cancellationToken));

    [HttpGet("copy-source")]
    public async Task<ActionResult<BudgetSnapshotResponse>> CopySource(Guid householdId, [FromQuery] BudgetCopySourceRequest request,
        CancellationToken cancellationToken)
    {
        var source = await snapshots.FindCopySourceAsync(householdId, request.EffectiveFrom!.Value, cancellationToken);
        return source is null ? NoContent() : Ok(BudgetSnapshotResponse.From(source));
    }

    [HttpGet("effective")]
    public async Task<ActionResult<BudgetEffectivePlanResponse>> Effective(Guid householdId, [FromQuery] BudgetAsOfRequest request,
        CancellationToken cancellationToken)
    {
        var date = AsOf(request.AsOf);
        var result = await snapshots.GetEffectiveAsync(householdId, date, cancellationToken);
        return result is null ? NoContent() : Ok(new BudgetEffectivePlanResponse(date,
            BudgetSnapshotResponse.From(result.Snapshot), result.EffectiveUntilExclusive));
    }

    [HttpGet("~/api/households/{householdId:guid}/budgets/current-outgoings")]
    public async Task<ActionResult<BudgetCurrentOutgoingsResponse>> CurrentOutgoings(Guid householdId,
        [FromQuery] BudgetCurrentOutgoingsRequest request, CancellationToken cancellationToken) =>
        BudgetCurrentOutgoingsResponse.From(await snapshots.GetCurrentOutgoingsAsync(householdId, AsOf(request.AsOf),
            request.IncludeRetired, cancellationToken));

    [HttpPut("{snapshotId:guid}/header")]
    public async Task<ActionResult<BudgetSnapshotResponse>> CorrectHeader(Guid householdId, Guid snapshotId,
        CorrectBudgetHeaderRequest request, CancellationToken cancellationToken) =>
        BudgetSnapshotResponse.From(await snapshots.CorrectHeaderAsync(householdId, snapshotId, request.ExpectedRevision!.Value,
            request.EffectiveFrom!.Value, request.Notes, cancellationToken));

    [HttpPost("{snapshotId:guid}/entries")]
    public async Task<ActionResult<BudgetSnapshotResponse>> AddEntry(Guid householdId, Guid snapshotId,
        ChangeBudgetEntryRequest request, CancellationToken cancellationToken) =>
        BudgetSnapshotResponse.From(await snapshots.AddEntryAsync(householdId, snapshotId, request.ExpectedRevision!.Value,
            request.Entry!.ToValues(), cancellationToken));

    [HttpPut("{snapshotId:guid}/entries/{entryId:guid}")]
    public async Task<ActionResult<BudgetSnapshotResponse>> CorrectEntry(Guid householdId, Guid snapshotId, Guid entryId,
        ChangeBudgetEntryRequest request, CancellationToken cancellationToken) =>
        BudgetSnapshotResponse.From(await snapshots.CorrectEntryAsync(householdId, snapshotId, request.ExpectedRevision!.Value,
            entryId, request.Entry!.ToValues(), cancellationToken));

    [HttpPut("{snapshotId:guid}/entries/{entryId:guid}/amount")]
    public async Task<ActionResult<BudgetSnapshotResponse>> CorrectAmount(Guid householdId, Guid snapshotId, Guid entryId,
        CorrectBudgetAmountRequest request, CancellationToken cancellationToken) =>
        BudgetSnapshotResponse.From(await snapshots.CorrectAmountAsync(householdId, snapshotId, request.ExpectedRevision!.Value,
            entryId, request.PlannedAmount, request.ConfirmedOn, request.Notes, cancellationToken));

    [HttpPut("{snapshotId:guid}/entries/{entryId:guid}/metadata")]
    public async Task<ActionResult<BudgetSnapshotResponse>> CorrectMetadata(Guid householdId, Guid snapshotId, Guid entryId,
        CorrectBudgetMetadataRequest request, CancellationToken cancellationToken) =>
        BudgetSnapshotResponse.From(await snapshots.CorrectEntryMetadataAsync(householdId, snapshotId, request.ExpectedRevision!.Value,
            entryId, request.Metadata!.ToValues(), cancellationToken));

    [HttpPost("{snapshotId:guid}/entries/{entryId:guid}/remove")]
    public async Task<ActionResult<BudgetSnapshotResponse>> RemoveEntry(Guid householdId, Guid snapshotId, Guid entryId,
        RemoveBudgetEntryRequest request, CancellationToken cancellationToken) =>
        BudgetSnapshotResponse.From(await snapshots.RemoveEntryAsync(householdId, snapshotId, request.ExpectedRevision!.Value,
            entryId, cancellationToken));

    private DateOnly AsOf(DateOnly? requested) => requested ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
