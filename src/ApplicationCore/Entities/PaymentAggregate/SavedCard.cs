using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A Finix payment instrument that belongs to a shopper. Only tokenized,
/// non-sensitive card metadata is stored here.
/// </summary>
public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(
        string buyerId,
        string finixPaymentInstrumentId,
        string brand,
        string last4,
        int expirationMonth,
        int expirationYear,
        string? cardholderName)
    {
        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardholderName = cardholderName;
        IsEnabled = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? CardholderName { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Disable()
    {
        IsEnabled = false;
    }
}
