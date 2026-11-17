using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card tokenized by Finix and linked to exactly one shopper. Only the safe
/// descriptor fields are stored locally; the PAN and security code never leave
/// the request that tokenized them.
/// </summary>
public class SavedCard : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() {}

    public SavedCard(
        string shopperId,
        string finixPaymentInstrumentId,
        string last4,
        string? brand,
        int expirationMonth,
        int expirationYear,
        string? fingerprint)
    {
        Guard.Against.NullOrEmpty(shopperId, nameof(shopperId));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        ShopperId = shopperId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Last4 = last4;
        Brand = brand;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        Fingerprint = fingerprint;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string ShopperId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string Last4 { get; private set; }
    public string? Brand { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? Fingerprint { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted()
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
