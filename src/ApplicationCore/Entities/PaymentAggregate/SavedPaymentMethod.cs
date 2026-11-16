using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A tokenized card held by Finix. Only non-sensitive display data is stored here;
/// the card number and CVC are never persisted by eShop.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    // Required by Entity Framework.
    private SavedPaymentMethod()
    {
    }

    public SavedPaymentMethod(string buyerId, string finixPaymentInstrumentId, string? brand,
        string? lastFour, int expirationMonth, int expirationYear, string? cardType, string? name)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));

        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        LastFour = lastFour;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardType = cardType;
        Name = name;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }

    public string FinixPaymentInstrumentId { get; private set; }

    public string? Brand { get; private set; }

    public string? LastFour { get; private set; }

    public int ExpirationMonth { get; private set; }

    public int ExpirationYear { get; private set; }

    public string? CardType { get; private set; }

    public string? Name { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkDeleted()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
