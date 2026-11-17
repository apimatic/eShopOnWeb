using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod()
    {
    }
#pragma warning restore CS8618

    public SavedPaymentMethod(
        string buyerId,
        string finixPaymentInstrumentId,
        string? finixIdentityId,
        string? brand,
        string? last4,
        int? expirationMonth,
        int? expirationYear,
        string? cardType,
        DateTimeOffset now)
    {
        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        FinixIdentityId = finixIdentityId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardType = cardType;
        IsEnabled = true;
        CreatedAt = now;
    }

    public string BuyerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string? FinixIdentityId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public string? CardType { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Disable()
    {
        IsEnabled = false;
    }
}
