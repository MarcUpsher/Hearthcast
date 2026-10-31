using Hearthcast.Api.FinancedAssets.Contracts;
using Hearthcast.Data.FinancedAssets;
using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.FinancedAssets;

[ApiController]
[Route("api/households/{householdId:guid}/financed-assets")]
public sealed class FinancedAssetsController(FinancedAssetService assets) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<FinancedAssetResponse>> Create(Guid householdId, CreateFinancedAssetRequest request,
        CancellationToken cancellationToken)
    {
        var asset = await assets.CreateAsync(householdId, request.GroupId!.Value, request.Name!, request.CurrencyCode!,
            request.CountryCode!, request.PurchaseDate, request.PurchasePrice, request.OriginalLoanAmount!.Value,
            request.HasWithdrawalFacility!.Value, Shares(request.Owners!), request.LoanHolderPersonIds!,
            request.IncludeInHousehold!.Value, Shares(request.HouseholdAllocations!), cancellationToken);
        return CreatedAtAction(nameof(Get), new { householdId, financedAssetId = asset.Id }, FinancedAssetResponse.From(asset));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FinancedAssetResponse>>> List(Guid householdId, CancellationToken cancellationToken) =>
        (await assets.ListAsync(householdId, cancellationToken)).Select(FinancedAssetResponse.From).ToArray();

    [HttpGet("{financedAssetId:guid}")]
    public async Task<ActionResult<FinancedAssetResponse>> Get(Guid householdId, Guid financedAssetId, CancellationToken cancellationToken) =>
        FinancedAssetResponse.From(await assets.GetAsync(householdId, financedAssetId, cancellationToken));

    [HttpPut("{financedAssetId:guid}/associations")]
    public async Task<ActionResult<FinancedAssetResponse>> SetAssociations(Guid householdId, Guid financedAssetId,
        SetFinancedAssetAssociationsRequest request, CancellationToken cancellationToken) =>
        FinancedAssetResponse.From(await assets.SetPersonAssociationsAsync(householdId, financedAssetId,
            Shares(request.Owners!), request.LoanHolderPersonIds!, request.IncludeInHousehold!.Value,
            Shares(request.HouseholdAllocations!), cancellationToken));

    [HttpPut("{financedAssetId:guid}/retirement")]
    public async Task<ActionResult<FinancedAssetResponse>> SetRetirement(Guid householdId, Guid financedAssetId,
        SetFinancedAssetRetirementRequest request, CancellationToken cancellationToken) =>
        FinancedAssetResponse.From(await assets.SetRetiredAsync(householdId, financedAssetId,
            request.IsRetired!.Value, cancellationToken));

    private static FinancedAssetShare[] Shares(IReadOnlyList<FinancedAssetShareRequest?> shares) =>
        shares.Select(share => new FinancedAssetShare(share!.PersonId!.Value, share.ShareBasisPoints!.Value)).ToArray();
}
