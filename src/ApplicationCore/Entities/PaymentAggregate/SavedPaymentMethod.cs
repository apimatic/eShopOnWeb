using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A Finix Payment Instrument that belongs to one eShop shopper. Only non-sensitive
/// display metadata is stored locally.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string finixPaymentInstrumentId, string brand,
        string last4, int expirationMonth, int expirationYear, string? name)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));

        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        Name = name;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string BuyerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string Brand { get; private set; }
    public string Last4 { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public string? Name { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkDeleted()
    {
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
