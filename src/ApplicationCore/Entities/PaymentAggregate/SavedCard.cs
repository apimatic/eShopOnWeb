using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A Finix payment instrument that belongs to an eShop shopper. Only non-sensitive
/// card metadata and Finix's tokenized identifier are persisted.
/// </summary>
public class SavedCard : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(string userId, string finixPaymentInstrumentId, string finixIdentityId,
        string? brand, string? last4, int expirationMonth, int expirationYear, string? cardholderName)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));
        Guard.Against.NullOrWhiteSpace(finixIdentityId, nameof(finixIdentityId));

        UserId = userId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        FinixIdentityId = finixIdentityId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string UserId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
