using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card saved by a shopper. Only the Finix payment-instrument token and safe
/// display metadata are stored; the PAN and CVV are never persisted.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string finixIdentityId, string finixPaymentInstrumentId,
        string? brand, string? lastFour, int? expirationMonth, int? expirationYear, string? name)
    {
        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        LastFour = lastFour;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        Name = name;
    }

    public string BuyerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastFour { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public string? Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
