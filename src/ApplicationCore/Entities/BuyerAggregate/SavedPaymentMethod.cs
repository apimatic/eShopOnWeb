using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string ownerId, string providerTokenId, string? brand, string? last4, string? expiry, string? alias)
    {
        OwnerId = ownerId;
        ProviderTokenId = providerTokenId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; private set; } = null!;
    public string ProviderTokenId { get; private set; } = null!;
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public string? Expiry { get; private set; }
    public string? Alias { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
