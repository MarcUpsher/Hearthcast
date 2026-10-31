using Hearthcast.Data.Summaries;
using Hearthcast.Data.Summaries.Models.IndividualHoldings;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Summaries;

[ApiController]
[Route("api/households/{householdId:guid}/snapshots/{snapshotId:guid}/individual-holdings")]
public sealed class IndividualHoldingsSummariesController(IndividualHoldingsSummaryService summaries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SnapshotIndividualHoldingsSummary>> Get(Guid householdId, Guid snapshotId,
        CancellationToken cancellationToken) =>
        await summaries.GetAsync(householdId, snapshotId, cancellationToken);
}
