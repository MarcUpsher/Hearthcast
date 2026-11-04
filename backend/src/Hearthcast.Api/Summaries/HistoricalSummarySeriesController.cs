using Hearthcast.Data.Summaries.Models.History;
using Hearthcast.Data.Summaries;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Summaries;

[ApiController]
[Route("api/households/{householdId:guid}/summary-history")]
public sealed class HistoricalSummarySeriesController(HistoricalSummarySeriesService history) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HistoricalSummarySeries>> Get(Guid householdId, [FromQuery] DateOnly from,
        [FromQuery] DateOnly to, [FromQuery] int intervalMonths, [FromQuery] string currency,
        CancellationToken cancellationToken) =>
        await history.GetAsync(householdId, from, to, intervalMonths, currency, cancellationToken);
}
