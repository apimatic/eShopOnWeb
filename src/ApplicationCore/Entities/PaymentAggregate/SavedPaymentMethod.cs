using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card saved with the payment processor. Only non-sensitive card metadata is persisted here.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string ownerId,
        string finixPaymentInstrumentId,
        string? brand,
        string? lastFour,
        int expirationMonth,
        int expirationYear,
        string? name)
    {
        Guard.Against.NullOrWhiteSpace(ownerId, nameof(ownerId));
        Guard.Against.NullOrWhiteSpace(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));

        OwnerId = ownerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        LastFour = lastFour;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        Name = name;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastFour { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? Name { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
