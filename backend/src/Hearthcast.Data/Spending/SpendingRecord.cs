using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

public sealed class SpendingRecord
{
    private readonly List<SpendingLine> _lines = [];
    private SpendingRecord() { }

    internal SpendingRecord(Guid id, Merchant merchant, DateOnly spendingDate, string currencyCode,
        IReadOnlyList<SpendingLineInput> lines, string creationRequestHash,
        SpendingRecordKind kind = SpendingRecordKind.Payment, SpendingRecord? originalPayment = null,
        decimal? receiptTotal = null, string? externalReference = null, string? notes = null)
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
    public decimal TotalAmount => _lines.Sum(l => l.NetAmount);
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
        line.CorrectValues(netAmount, quantity, unitPrice, promotionStatus, notes);
        Touch();
    }

    // The caller removes these old rows and detaches informational price sources in one transaction.
    internal IReadOnlyList<SpendingLine> ReplaceLines(Guid expectedRevision, IReadOnlyList<SpendingLineInput> lines)
    {
        RequireEditable(expectedRevision);
        var replacements = BuildLines(lines);
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
        string? externalReference, string? notes)
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
        Touch();
        return removed;
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
