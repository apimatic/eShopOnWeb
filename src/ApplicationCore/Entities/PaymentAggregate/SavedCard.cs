using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedCard : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private SavedCard() { }

    public SavedCard(
        string ownerId,
        string finixPaymentInstrumentId,
        string brand,
        string lastFour,
        int expirationMonth,
        int expirationYear,
        string? cardType,
        DateTimeOffset createdAt)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));
        Guard.Against.NullOrEmpty(lastFour, nameof(lastFour));

        OwnerId = ownerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        LastFour = lastFour;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardType = cardType;
        CreatedAt = createdAt;
        Enabled = true;
    }

    public string OwnerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string Brand { get; private set; }
    public string LastFour { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? CardType { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Disable()
    {
        Enabled = false;
    }
}
