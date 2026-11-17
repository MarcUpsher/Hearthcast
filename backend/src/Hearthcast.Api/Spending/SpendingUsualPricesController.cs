using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Data.Spending;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Spending;

[ApiController]
[Route("api/households/{householdId:guid}/spending/usual-prices")]
public sealed class SpendingUsualPricesController(SpendingSetupService setup) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<UsualPriceResponse>> Create(Guid householdId, CreateUsualPriceRequest request,
        CancellationToken cancellationToken)
    {
        var price = await setup.CreateUsualPriceAsync(householdId, request.SpendingItemId!.Value,
            request.MerchantId!.Value, request.CurrencyCode!, request.UsualUnitPrice!.Value, cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, priceId = price.Id }, UsualPriceResponse.From(price));
    }

    [HttpGet("{priceId:guid}")]
    public async Task<ActionResult<UsualPriceResponse>> Get(Guid householdId, Guid priceId,
        CancellationToken cancellationToken) =>
        UsualPriceResponse.From(await setup.GetUsualPriceAsync(householdId, priceId, cancellationToken));

    [HttpPut("{priceId:guid}")]
    public async Task<ActionResult<UsualPriceResponse>> Update(Guid householdId, Guid priceId,
        UpdateUsualPriceRequest request, CancellationToken cancellationToken) =>
        UsualPriceResponse.From(await setup.UpdateUsualPriceAsync(householdId, priceId, request.ExpectedRevision!.Value,
            request.UsualUnitPrice!.Value, cancellationToken));
}
