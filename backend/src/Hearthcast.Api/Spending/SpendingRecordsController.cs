using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Data.Spending;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Spending;

[ApiController]
[Route("api/households/{householdId:guid}/spending/records")]
public sealed class SpendingRecordsController(SpendingRecordService records) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<SpendingRecordResponse>> Create(Guid householdId, CreateSpendingRecordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await records.CreateAsync(householdId, request.Id!.Value, request.ToValues(),
            Prices(request.RememberPrices), cancellationToken);
        var response = await ResponseAsync(householdId, result.Record, cancellationToken);
        return result.WasCreated ? CreatedAtAction(nameof(Get), new { householdId, recordId = response.Id }, response) : Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<SpendingHistoryPageResponse>> List(Guid householdId,
        [FromQuery] SpendingHistoryRequest request, CancellationToken cancellationToken)
    {
        var page = await records.ListPageAsync(householdId, request.From, request.To, request.IncludeVoided,
            request.PageSize, request.Cursor, cancellationToken);
        var states = await OriginalStatesAsync(householdId, page.Records, cancellationToken);
        return new SpendingHistoryPageResponse(page.Records.Select(r => Map(r, states)).ToArray(), page.NextCursor, page.PageSize);
    }

    [HttpGet("{recordId:guid}")]
    public async Task<ActionResult<SpendingRecordResponse>> Get(Guid householdId, Guid recordId, CancellationToken cancellationToken) =>
        await ResponseAsync(householdId, await records.GetAsync(householdId, recordId, cancellationToken), cancellationToken);

    [HttpPut("{recordId:guid}/header")]
    public async Task<ActionResult<SpendingRecordResponse>> CorrectHeader(Guid householdId, Guid recordId,
        CorrectSpendingHeaderRequest request, CancellationToken cancellationToken) =>
        await ResponseAsync(householdId, await records.CorrectHeaderAsync(householdId, recordId, request.ExpectedRevision!.Value,
            request.SpendingDate!.Value, request.ReceiptTotal, request.ExternalReference, request.Notes, cancellationToken), cancellationToken);

    [HttpPut("{recordId:guid}/lines/{lineId:guid}")]
    public async Task<ActionResult<SpendingRecordResponse>> CorrectLine(Guid householdId, Guid recordId, Guid lineId,
        CorrectSpendingLineRequest request, CancellationToken cancellationToken) =>
        await ResponseAsync(householdId, await records.CorrectLineAsync(householdId, recordId, request.ExpectedRevision!.Value,
            lineId, request.NetAmount!.Value, request.Quantity, request.UnitPrice, request.PromotionStatus!.Value,
            request.Notes, Prices(request.RememberPrices), cancellationToken), cancellationToken);

    [HttpPut("{recordId:guid}")]
    public async Task<ActionResult<SpendingRecordResponse>> Replace(Guid householdId, Guid recordId,
        ReplaceSpendingRecordRequest request, CancellationToken cancellationToken) =>
        await ResponseAsync(householdId, await records.ReplaceAsync(householdId, recordId, request.ExpectedRevision!.Value,
            request.MerchantId!.Value, request.SpendingDate!.Value, request.Lines!.Select(l => l!.ToValues()).ToArray(),
            request.ReceiptTotal, request.ExternalReference, request.Notes, Prices(request.RememberPrices), cancellationToken,
            request.Discounts?.Select(d => d!.ToValues()).ToArray()), cancellationToken);

    [HttpPut("{recordId:guid}/discounts")]
    public async Task<ActionResult<SpendingRecordResponse>> CorrectDiscounts(Guid householdId, Guid recordId,
        CorrectSpendingDiscountsRequest request, CancellationToken cancellationToken) =>
        await ResponseAsync(householdId, await records.CorrectDiscountsAsync(householdId, recordId, request.ExpectedRevision!.Value,
            request.Discounts!.Select(d => d!.ToValues()).ToArray(), cancellationToken), cancellationToken);

    [HttpPost("{recordId:guid}/void")]
    public async Task<ActionResult<SpendingRecordResponse>> Void(Guid householdId, Guid recordId,
        VoidSpendingRecordRequest request, CancellationToken cancellationToken) =>
        await ResponseAsync(householdId, await records.VoidAsync(householdId, recordId, request.ExpectedRevision!.Value, cancellationToken), cancellationToken);

    [HttpGet("~/api/households/{householdId:guid}/spending/items/{itemId:guid}/last-paid-price")]
    public async Task<ActionResult<LastPaidPriceResponse>> LastPaid(Guid householdId, Guid itemId,
        [FromQuery] FindLastPaidPriceRequest request, CancellationToken cancellationToken)
    {
        var price = await records.FindLastPaidAsync(householdId, itemId, request.MerchantId!.Value,
            request.CurrencyCode!, request.OnOrBefore!.Value, request.ExcludeRecordId, cancellationToken);
        return price is null ? NoContent() : Ok(LastPaidPriceResponse.From(price));
    }

    [HttpGet("~/api/households/{householdId:guid}/spending/currencies")]
    public async Task<ActionResult<IReadOnlyList<SpendingCurrencyResponse>>> Currencies(Guid householdId,
        CancellationToken cancellationToken) =>
        (await records.ListSupportedCurrenciesAsync(householdId, cancellationToken)).OrderBy(c => c.Key)
            .Select(c => new SpendingCurrencyResponse(c.Key, c.Value)).ToArray();

    private static RememberSpendingPrice[]? Prices(IReadOnlyList<RememberSpendingPriceRequest?>? requests) =>
        requests?.Select(p => p!.ToValues()).ToArray();

    private async Task<SpendingRecordResponse> ResponseAsync(Guid householdId, SpendingRecord record, CancellationToken token) =>
        Map(record, await OriginalStatesAsync(householdId, [record], token));

    private Task<IReadOnlyDictionary<Guid, bool>> OriginalStatesAsync(Guid householdId, IReadOnlyList<SpendingRecord> page, CancellationToken token)
    {
        var ids = page.Where(r => r.OriginalSpendingRecordId.HasValue).Select(r => r.OriginalSpendingRecordId!.Value).Distinct().ToArray();
        return ids.Length == 0 ? Task.FromResult<IReadOnlyDictionary<Guid, bool>>(new Dictionary<Guid, bool>())
            : records.GetOriginalPaymentVoidStatesAsync(householdId, ids, token);
    }

    private static SpendingRecordResponse Map(SpendingRecord record, IReadOnlyDictionary<Guid, bool> states) =>
        SpendingRecordResponse.From(record, record.OriginalSpendingRecordId.HasValue ? states[record.OriginalSpendingRecordId.Value] : null);
}
