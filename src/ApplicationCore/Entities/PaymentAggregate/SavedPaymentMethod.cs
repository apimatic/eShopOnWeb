using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public string? Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId,
        string finixPaymentInstrumentId,
        string finixIdentityId,
        string? brand,
        string? last4,
        int? expirationMonth,
        int? expirationYear,
        string? name)
    {
        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        FinixIdentityId = finixIdentityId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        Name = name;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
