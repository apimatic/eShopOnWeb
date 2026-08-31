using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string ownerId, string providerTokenId, string? providerCustomerId,
        string? brand, string last4, string? expiry, string providerStatus)
    {
        OwnerId = ownerId;
        ProviderTokenId = providerTokenId;
        ProviderCustomerId = providerCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        ProviderStatus = providerStatus;
    }

    public string OwnerId { get; private set; }
    public string ProviderTokenId { get; private set; }
    public string? ProviderCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string ProviderStatus { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
        DeletedAt ??= DateTimeOffset.UtcNow;
    }
}
