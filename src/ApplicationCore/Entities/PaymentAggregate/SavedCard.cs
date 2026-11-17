using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedCard() { }
#pragma warning restore CS8618

    public SavedCard(string ownerId,
        string finixPaymentInstrumentId,
        string? brand,
        string? lastFour,
        int? expirationMonth,
        int? expirationYear)
    {
        OwnerId = ownerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        LastFour = lastFour;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; private set; } = string.Empty;
    public string? Brand { get; private set; }
    public string? LastFour { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Disable()
    {
        IsActive = false;
    }
}
