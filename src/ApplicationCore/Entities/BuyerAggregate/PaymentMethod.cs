using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private PaymentMethod() { }

    public PaymentMethod(string buyerId, string providerTokenId, string providerCustomerId,
        string? brand, string? lastDigits, string? expiry, string? cardType)
    {
        BuyerId = buyerId;
        ProviderTokenId = providerTokenId;
        ProviderCustomerId = providerCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardType = cardType;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public string BuyerId { get; private set; }
    public string ProviderTokenId { get; private set; }
    public string ProviderCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardType { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RefreshSafeDetails(string? brand, string? lastDigits, string? expiry, string? cardType)
    {
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardType = cardType;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
