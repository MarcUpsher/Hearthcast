using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

public sealed class SpendingRecord
{
    private readonly List<SpendingLine> _lines = [];
    private readonly List<SpendingDiscount> _discounts = [];
    private SpendingRecord() { }

    internal SpendingRecord(Guid id, Merchant merchant, DateOnly spendingDate, string currencyCode,
        IReadOnlyList<SpendingLineInput> lines, string creationRequestHash,
        SpendingRecordKind kind = SpendingRecordKind.Payment, SpendingRecord? originalPayment = null,
        decimal? receiptTotal = null, string? externalReference = null, string? notes = null,
        IReadOnlyList<SpendingDiscountInput>? discounts = null)
    {
        if (id == Guid.Empty) throw new InvalidValueException("Supply the client-generated record identifier.");
        if (!Enum.IsDefined(kind)) throw new InvalidValueException("Select Payment or Refund.");
        if (creationRequestHash is null || creationRequestHash.Length != 64 || creationRequestHash.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f')))
            throw new InvalidValueException("Supply a canonical SHA-256 create-request hash.");
        if (originalPayment is not null && (kind != SpendingRecordKind.Refund || originalPayment.Kind != SpendingRecordKind.Payment
            || originalPayment.HouseholdId != merchant.HouseholdId || originalPayment.Id == id || originalPayment.IsVoided))
            throw new InvalidValueException("A refund can reference a different, nonvoid payment in this household.");
        Id = id;
        HouseholdId = merchant.HouseholdId;
        MerchantId = merchant.Id;
        MerchantName = merchant.Name;
        CurrencyCode = SpendingCurrencyPolicy.Normalize(currencyCode);
        CurrencyMinorUnits = SpendingCurrencyPolicy.MinorUnits(CurrencyCode);
        Kind = kind;
        OriginalSpendingRecordId = originalPayment?.Id;
        OriginalSpendingRecordKind = originalPayment?.Kind;
        CreationRequestHash = creationRequestHash;
        SetHeader(spendingDate, receiptTotal, externalReference, notes);
        _lines.AddRange(BuildLines(lines));
        _discounts.AddRange(BuildDiscounts(discounts ?? []));
        ValidateDiscounts(_lines, _discounts);
        CreatedAtUtc = SpendingValueRules.Now();
        UpdatedAtUtc = CreatedAtUtc;
        Revision = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public SpendingRecordKind Kind { get; private set; }
    public DateOnly SpendingDate { get; private set; }
    public Guid MerchantId { get; private set; }
    public string MerchantName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public int CurrencyMinorUnits { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? Notes { get; private set; }
    public decimal? ReceiptTotal { get; private set; }
    public Guid? OriginalSpendingRecordId { get; private set; }
    public SpendingRecordKind? OriginalSpendingRecordKind { get; private set; }
    public string CreationRequestHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid Revision { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public bool IsVoided => VoidedAtUtc.HasValue;
    public IReadOnlyList<SpendingLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<SpendingDiscount> Discounts => _discounts.AsReadOnly();
    public decimal PurchaseAmount => _lines.Where(l => !l.IsAdditionalCost).Sum(l => l.NetAmount);
    public decimal AdditionalCostAmount => _lines.Where(l => l.IsAdditionalCost).Sum(l => l.NetAmount);
    public decimal GrossAmount => _lines.Sum(l => l.NetAmount);
    public decimal DiscountAmount => _discounts.Sum(d => d.Amount);
    public decimal TotalAmount => GrossAmount - DiscountAmount;
    public decimal CountedAmount => IsVoided ? 0 : Kind == SpendingRecordKind.Refund ? -TotalAmount : TotalAmount;
    public decimal? ReceiptDifference => ReceiptTotal - TotalAmount;
    public string ReconciliationStatus => ReceiptTotal is null ? "NotChecked" : ReceiptDifference == 0 ? "Reconciled" : "Unreconciled";

    internal void CorrectHeader(Guid expectedRevision, DateOnly spendingDate, decimal? receiptTotal,
        string? externalReference, string? notes)
    {
        RequireEditable(expectedRevision);
        SetHeader(spendingDate, receiptTotal, externalReference, notes);
        Touch();
    }

    internal void CorrectLine(Guid expectedRevision, Guid lineId, decimal netAmount, decimal? quantity,
        decimal? unitPrice, PromotionStatus promotionStatus, string? notes)
    {
        RequireEditable(expectedRevision);
        var line = _lines.SingleOrDefault(l => l.Id == lineId)
            ?? throw new InvalidValueException("The line does not belong to this record.");
        ValidateDiscounts(_lines, _discounts, line.Id, netAmount);
        line.CorrectValues(netAmount, quantity, unitPrice, promotionStatus, notes);
        Touch();
    }

    // The caller removes these old rows and detaches informational price sources in one transaction.
    internal IReadOnlyList<SpendingLine> ReplaceLines(Guid expectedRevision, IReadOnlyList<SpendingLineInput> lines)
    {
        RequireEditable(expectedRevision);
        var replacements = BuildLines(lines);
        ValidateDiscounts(replacements, _discounts);
        var removed = _lines.ToArray();
        _lines.Clear();
        _lines.AddRange(replacements);
        Touch();
        return removed;
    }

    // Merchant changes and deliberate metadata refresh require explicit replacement lines.
    // Currency/kind identify this record; a mistake in those is voided and recreated.
    internal IReadOnlyList<SpendingLine> Replace(Guid expectedRevision, Merchant merchant, DateOnly spendingDate,
        IReadOnlyList<SpendingLineInput> lines, decimal? receiptTotal,
        string? externalReference, string? notes, IReadOnlyList<SpendingDiscountInput>? discounts = null)
    {
        RequireEditable(expectedRevision);
        if (merchant.HouseholdId != HouseholdId) throw new InvalidValueException("The merchant must belong to this household.");
        var candidate = new SpendingRecord
        {
            Id = Id, HouseholdId = HouseholdId, CurrencyCode = CurrencyCode,
            CurrencyMinorUnits = CurrencyMinorUnits
        };
        candidate.SetHeader(spendingDate, receiptTotal, externalReference, notes);
        var replacements = candidate.BuildLines(lines);
        var replacementDiscounts = discounts is null ? _discounts.ToArray() : candidate.BuildDiscounts(discounts);
        ValidateDiscounts(replacements, replacementDiscounts);
        var removed = _lines.ToArray();
        MerchantId = merchant.Id;
        MerchantName = merchant.Name;
        CurrencyCode = candidate.CurrencyCode;
        CurrencyMinorUnits = candidate.CurrencyMinorUnits;
        SpendingDate = candidate.SpendingDate;
        ReceiptTotal = candidate.ReceiptTotal;
        ExternalReference = candidate.ExternalReference;
        Notes = candidate.Notes;
        _lines.Clear();
        _lines.AddRange(replacements);
        if (discounts is not null) { _discounts.Clear(); _discounts.AddRange(replacementDiscounts); }
        Touch();
        return removed;
    }

    internal IReadOnlyList<SpendingDiscount> CorrectDiscounts(Guid expectedRevision, IReadOnlyList<SpendingDiscountInput> discounts)
    {
        RequireEditable(expectedRevision);
        var replacements = BuildDiscounts(discounts); ValidateDiscounts(_lines, replacements);
        var removed = _discounts.ToArray(); _discounts.Clear(); _discounts.AddRange(replacements); Touch(); return removed;
    }

    private SpendingDiscount[] BuildDiscounts(IReadOnlyList<SpendingDiscountInput> discounts)
    {
        ArgumentNullException.ThrowIfNull(discounts);
        if (discounts.Any(d => d is null)) throw new InvalidValueException("Discounts cannot contain null entries.");
        return discounts.Select((discount, index) => new SpendingDiscount(this, index + 1, discount)).ToArray();
    }

    private static void ValidateDiscounts(IReadOnlyList<SpendingLine> lines, IReadOnlyList<SpendingDiscount> discounts, Guid? changedLine = null, decimal? changedAmount = null)
    {
        foreach (var category in discounts.GroupBy(d => d.ExpenseCategoryId))
        {
            var gross = lines.Where(l => l.ExpenseCategoryId == category.Key).Sum(l => l.Id == changedLine ? changedAmount!.Value : l.NetAmount);
            if (category.Sum(d => d.Amount) > gross) throw new InvalidValueException("Receipt discounts cannot exceed the entered lines in their category.");
        }
    }

    internal void Void(Guid expectedRevision)
    {
        if (Revision != expectedRevision) throw new SpendingRecordConflictException();
        if (IsVoided) return;
        VoidedAtUtc = SpendingValueRules.Now();
        Touch();
    }

    private SpendingLine[] BuildLines(IReadOnlyList<SpendingLineInput> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(l => l is null)) throw new InvalidValueException("Supply at least one spending line.");
        return lines.Select((line, index) => new SpendingLine(this, index + 1, line)).ToArray();
    }

    private void SetHeader(DateOnly spendingDate, decimal? receiptTotal, string? externalReference, string? notes)
    {
        if (spendingDate == default) throw new InvalidValueException("Supply a purchase/payment date.");
        var amount = receiptTotal.HasValue ? SpendingValueRules.Amount(receiptTotal.Value, CurrencyMinorUnits) : (decimal?)null;
        var reference = SpendingSetupRules.Notes(externalReference);
        var savedNotes = SpendingSetupRules.Notes(notes);
        SpendingDate = spendingDate;
        ReceiptTotal = amount;
        ExternalReference = reference;
        Notes = savedNotes;
    }

    private void RequireEditable(Guid expectedRevision)
    {
        if (Revision != expectedRevision) throw new SpendingRecordConflictException();
        if (IsVoided) throw new BusinessRuleViolationException("A voided spending record cannot be edited.");
    }

    private void Touch()
    {
        Revision = Guid.NewGuid();
        UpdatedAtUtc = SpendingValueRules.Now();
    }
}
