using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Data.Spending;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Spending;

[ApiController]
[Route("api/households/{householdId:guid}/spending/merchants")]
public sealed class SpendingMerchantsController(SpendingSetupService setup) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<MerchantResponse>> Create(Guid householdId, SpendingNameRequest request,
        CancellationToken cancellationToken)
    {
        var merchant = await setup.CreateMerchantAsync(householdId, request.Name!, cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, merchantId = merchant.Id }, MerchantResponse.From(merchant));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MerchantResponse>>> List(Guid householdId,
        CancellationToken cancellationToken, [FromQuery] bool includeRetired = true) =>
        (await setup.ListMerchantsAsync(householdId, includeRetired, cancellationToken)).Select(MerchantResponse.From).ToArray();

    [HttpGet("{merchantId:guid}")]
    public async Task<ActionResult<MerchantResponse>> Get(Guid householdId, Guid merchantId,
        CancellationToken cancellationToken) =>
        MerchantResponse.From(await setup.GetMerchantAsync(householdId, merchantId, cancellationToken));

    [HttpPut("{merchantId:guid}/name")]
    public async Task<ActionResult<MerchantResponse>> Rename(Guid householdId, Guid merchantId,
        SpendingNameRequest request, CancellationToken cancellationToken) =>
        MerchantResponse.From(await setup.RenameMerchantAsync(householdId, merchantId, request.Name!, cancellationToken));

    [HttpPut("{merchantId:guid}/retirement")]
    public async Task<ActionResult<MerchantResponse>> SetRetirement(Guid householdId, Guid merchantId,
        SpendingRetirementRequest request, CancellationToken cancellationToken) =>
        MerchantResponse.From(await setup.SetMerchantRetiredAsync(householdId, merchantId, request.IsRetired!.Value, cancellationToken));
}
