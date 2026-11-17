using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card saved at Finix for a shopper. Only safe, non-sensitive display data
/// and the Finix Payment Instrument id are stored.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(
        string ownerId,
        string finixPaymentInstrumentId,
        string? brand,
        string last4,
        int expirationMonth,
        int expirationYear,
        string? cardholderName,
        string? alias)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));
        Guard.Against.NullOrEmpty(last4, nameof(last4));

        OwnerId = ownerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardholderName = cardholderName;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string? Brand { get; private set; }
    public string Last4 { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? CardholderName { get; private set; }
    public string? Alias { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
