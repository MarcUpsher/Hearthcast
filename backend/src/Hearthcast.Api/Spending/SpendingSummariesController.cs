using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Data.Spending;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Spending;

[ApiController]
[Route("api/households/{householdId:guid}/spending/summary")]
public sealed class SpendingSummariesController(SpendingCategorySummaryService summaries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SpendingCategorySummaryResponse>> Get(Guid householdId,
        [FromQuery] SpendingCategorySummaryRequest request, CancellationToken cancellationToken) =>
        SpendingCategorySummaryResponse.FromSummary(await summaries.GetAsync(householdId,
            request.From!.Value, request.To!.Value, cancellationToken));
}
