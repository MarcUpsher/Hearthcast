namespace Hearthcast.Data.Spending;

/// <summary>Null revision means create only; a supplied revision authorises replacing an existing usual price.</summary>
public sealed record RememberSpendingPrice(int LinePosition, decimal ChosenUnitPrice, Guid? ExpectedRevision = null);
