using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedCard : BaseEntity, IAggregateRoot
{
    private SavedCard()
    {
    }

    public SavedCard(
        string buyerId,
        string finixIdentityId,
        string finixPaymentInstrumentId,
        string brand,
        string last4,
        int expirationMonth,
        int expirationYear,
        string? cardholderName,
        string? alias,
        DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardholderName = cardholderName;
        Alias = alias;
        CreatedAt = createdAt;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string FinixIdentityId { get; private set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? CardholderName { get; private set; }
    public string? Alias { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset deletedAt)
    {
        IsDeleted = true;
        DeletedAt = deletedAt;
    }
}
