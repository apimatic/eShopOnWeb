using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedCard : BaseEntity, IAggregateRoot
{
    private SavedCard() { }

    public SavedCard(string buyerId, string finixPaymentInstrumentId, string? brand, string? lastFour,
        int? expirationMonth, int? expirationYear, string? cardType, string? name)
    {
        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        LastFour = lastFour;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardType = cardType;
        Name = name;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; private set; } = string.Empty;
    public string? Brand { get; private set; }
    public string? LastFour { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public string? CardType { get; private set; }
    public string? Name { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkDeleted()
    {
        IsDeleted = true;
    }
}
