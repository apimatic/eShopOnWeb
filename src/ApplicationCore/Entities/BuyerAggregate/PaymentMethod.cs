using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity, IAggregateRoot
{
    private PaymentMethod() { }

    public PaymentMethod(string buyerId)
    {
        BuyerId = buyerId;
        PaymentReference = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
        State = PaymentMethodState.Pending;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PaymentReference { get; private set; } = string.Empty;
    public string? ProviderTokenId { get; private set; }
    public string? ProviderCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public PaymentMethodState State { get; private set; }

    public void Activate(string providerTokenId, string? providerCustomerId, string brand, string last4,
        string? expiry)
    {
        ProviderTokenId = providerTokenId;
        ProviderCustomerId = providerCustomerId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        State = PaymentMethodState.Active;
    }

    public void MarkDeleted(bool providerCleanupPending)
    {
        State = providerCleanupPending ? PaymentMethodState.PendingProviderDeletion : PaymentMethodState.Deleted;
    }
}

public enum PaymentMethodState
{
    Pending,
    Active,
    PendingProviderDeletion,
    Deleted
}
