using Hearthcast.Api.Snapshots.Contracts;
using Hearthcast.Data.Snapshots;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Snapshots;

[ApiController]
[Route("api/households/{householdId:guid}/snapshots")]
public sealed class SnapshotsController(SnapshotService snapshots, SnapshotActivityService activity) : ControllerBase
{
    [HttpGet("activity")]
    public async Task<ActionResult<SnapshotActivity>> Activity(Guid householdId, CancellationToken cancellationToken) =>
        await activity.GetAsync(householdId,cancellationToken);

    [HttpPost]
    public async Task<ActionResult<SnapshotResponse>> Create(Guid householdId, CreateSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.CreateAsync(householdId, request.SnapshotDate!.Value,
            ItemValues(request.Values), request.DatePrecision, FinancedAssetValues(request.FinancedAssetValues),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, snapshotId = snapshot.Id }, SnapshotResponse.From(snapshot));
    }

    [HttpPost("next")]
    public async Task<ActionResult<SnapshotResponse>> CreateNext(Guid householdId, CreateNextSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.CreateNextAsync(householdId, request.SnapshotDate!.Value,
            ItemValues(request.Values), request.DatePrecision, request.SourceSnapshotId,
            FinancedAssetValues(request.FinancedAssetValues),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, snapshotId = snapshot.Id }, SnapshotResponse.From(snapshot));
    }

    [HttpPatch("{snapshotId:guid}/values")]
    public async Task<ActionResult<SnapshotResponse>> CorrectValues(Guid householdId, Guid snapshotId,
        CorrectSnapshotValuesRequest request, CancellationToken cancellationToken) =>
        SnapshotResponse.From(await snapshots.CorrectValuesAsync(householdId, snapshotId, request.ExpectedRevision!.Value,
            ItemValues(request.Values), FinancedAssetValues(request.FinancedAssetValues), cancellationToken));

    [HttpGet("{snapshotId:guid}")]
    public async Task<ActionResult<SnapshotResponse>> Get(Guid householdId, Guid snapshotId, CancellationToken cancellationToken) =>
        SnapshotResponse.From(await snapshots.GetAsync(householdId, snapshotId, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SnapshotInventoryEntry>>> List(Guid householdId, CancellationToken cancellationToken) =>
        (await snapshots.ListAsync(householdId, cancellationToken)).ToArray();

    private static ItemSnapshotValue[]? ItemValues(IReadOnlyList<ItemValueRequest?>? values) =>
        values?.Select(row => new ItemSnapshotValue(row!.TrackedItemId!.Value, row.Amount)).ToArray();

    private static FinancedAssetSnapshotValue[]? FinancedAssetValues(IReadOnlyList<FinancedAssetValueRequest?>? values) =>
        values?.Select(row => new FinancedAssetSnapshotValue(row!.FinancedAssetId!.Value,
            row.Balance!.Value, row.Amount)).ToArray();
}
