using Hearthcast.Data.Summaries;
using Hearthcast.Data.Summaries.Models;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Summaries;

[ApiController]
[Route("api/households/{householdId:guid}/snapshots/{snapshotId:guid}/summary")]
public sealed class SnapshotSummariesController(SnapshotSummaryService summaries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SnapshotSummary>> Get(Guid householdId, Guid snapshotId,
        CancellationToken cancellationToken) =>
        await summaries.GetAsync(householdId, snapshotId, cancellationToken);
}
