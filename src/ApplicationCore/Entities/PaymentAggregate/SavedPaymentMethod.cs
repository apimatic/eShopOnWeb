using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string buyerId,
        string finixPaymentInstrumentId,
        string? brand,
        string? lastFour,
        int? expirationMonth,
        int? expirationYear,
        string? cardholderName)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));

        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        LastFour = lastFour;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastFour { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
