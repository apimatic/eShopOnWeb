using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A saved card. Only the Finix payment instrument identifier and safe card
/// metadata are stored; the card number and security code never reach the
/// application database.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string buyerId,
        string finixIdentityId,
        string finixPaymentInstrumentId,
        string? brand,
        string? last4,
        string? cardType,
        int expirationMonth,
        int expirationYear)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(finixIdentityId, nameof(finixIdentityId));
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));

        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
        CardId = finixPaymentInstrumentId;
        Brand = brand;
        Last4 = last4;
        CardType = cardType;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        Alias = string.IsNullOrEmpty(brand)
            ? $"Card ending {last4}"
            : $"{brand} ending {last4}";
        Enabled = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public string? Alias { get; private set; }
    public string? CardId { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? CardType { get; private set; }
    public int ExpirationMonth { get; private set; }
    public int ExpirationYear { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Disable()
    {
        Enabled = false;
    }
}
