using Hearthcast.Api.Snapshots.Contracts;
using Hearthcast.Data.Snapshots;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Snapshots;

[ApiController]
[Route("api/households/{householdId:guid}/snapshots")]
public sealed class SnapshotsController(SnapshotService snapshots) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SnapshotResponse>> Create(Guid householdId, CreateSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.CreateAsync(householdId, request.SnapshotDate!.Value,
            request.Values?.Select(row => new ItemSnapshotValue(row!.TrackedItemId!.Value, row.Amount)).ToArray(),
            request.DatePrecision, request.FinancedAssetValues?.Select(row =>
                new FinancedAssetSnapshotValue(row!.FinancedAssetId!.Value, row.Balance!.Value, row.Amount)).ToArray(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, snapshotId = snapshot.Id }, SnapshotResponse.From(snapshot));
    }

    [HttpGet("{snapshotId:guid}")]
    public async Task<ActionResult<SnapshotResponse>> Get(Guid householdId, Guid snapshotId, CancellationToken cancellationToken) =>
        SnapshotResponse.From(await snapshots.GetAsync(householdId, snapshotId, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SnapshotInventoryEntry>>> List(Guid householdId, CancellationToken cancellationToken) =>
        (await snapshots.ListAsync(householdId, cancellationToken)).ToArray();
}
