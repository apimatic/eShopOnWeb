using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

#pragma warning disable CS8618 // Required by Entity Framework

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string finixPaymentInstrumentId, string? brand, string? last4, int expirationMonth, int expirationYear, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));

        BuyerId = buyerId;
        CardId = finixPaymentInstrumentId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string? Alias { get; private set; }
    public string? CardId { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
