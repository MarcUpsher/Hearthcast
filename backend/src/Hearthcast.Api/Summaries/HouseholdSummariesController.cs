using Hearthcast.Data.Summaries;
using Hearthcast.Data.Summaries.Models.Household;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Summaries;

[ApiController]
[Route("api/households/{householdId:guid}/snapshots/{snapshotId:guid}/household-summary")]
public sealed class HouseholdSummariesController(HouseholdSummaryService summaries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SnapshotHouseholdSummary>> Get(Guid householdId, Guid snapshotId,
        CancellationToken cancellationToken) =>
        await summaries.GetAsync(householdId, snapshotId, cancellationToken);
}
