using Hearthcast.Data.Summaries;
using Hearthcast.Data.Summaries.Models.Merged;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Summaries;

[ApiController]
[Route("api/households/{householdId:guid}/snapshots/{snapshotId:guid}/merged-summary")]
public sealed class MergedSnapshotSummariesController(MergedSnapshotSummaryService summaries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MergedSnapshotSummary>> Get(Guid householdId, Guid snapshotId, CancellationToken cancellationToken) =>
        await summaries.GetAsync(householdId, snapshotId, cancellationToken);
}
