using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() {}

    public PaymentMethod(string buyerId,
        string finixPaymentInstrumentId,
        string? brand,
        string? last4,
        int? expirationMonth,
        int? expirationYear,
        string? cardholderName,
        string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));

        BuyerId = buyerId;
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
        Brand = brand;
        Last4 = last4;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        CardholderName = cardholderName;
        Alias = alias;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string FinixPaymentInstrumentId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public string? CardholderName { get; private set; }
    public string? Alias { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
    }
}
