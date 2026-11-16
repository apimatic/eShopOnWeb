using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card the shopper saved for reuse. Only non-sensitive card metadata is stored here;
/// the card number and security code live exclusively inside Finix.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string buyerId,
        string finixPaymentInstrumentId,
        string? brand,
        string? lastFour,
        int? expirationMonth,
        int? expirationYear,
        string? cardholderName,
        string? fingerprint)
    {
        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        LastFour = lastFour;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardholderName = cardholderName;
        Fingerprint = fingerprint;
    }

    public string BuyerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastFour { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public string? CardholderName { get; private set; }
    public string? Fingerprint { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; private set; } = true;

    public void Deactivate() => IsActive = false;
}
