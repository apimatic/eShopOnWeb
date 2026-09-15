using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string buyerId, string providerPaymentTokenId, string providerCustomerId,
        string brand, string lastDigits, string expiry, string? cardholderName, string? cardType)
    {
        BuyerId = buyerId;
        ProviderPaymentTokenId = providerPaymentTokenId;
        ProviderCustomerId = providerCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        CardType = cardType;
    }

    public string BuyerId { get; private set; }
    public string ProviderPaymentTokenId { get; private set; }
    public string ProviderCustomerId { get; private set; }
    public string Brand { get; private set; }
    public string LastDigits { get; private set; }
    public string Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public string? CardType { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
        DeletedAt = DateTimeOffset.UtcNow;
    }
}
