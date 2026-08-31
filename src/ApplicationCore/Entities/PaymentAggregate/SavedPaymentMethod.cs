using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string paypalVaultId, string paypalCustomerId, string brand,
        string lastFour, string expiry)
    {
        BuyerId = buyerId;
        PayPalVaultId = paypalVaultId;
        PayPalCustomerId = paypalCustomerId;
        Brand = brand;
        LastFour = lastFour;
        Expiry = expiry;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string BuyerId { get; private set; } = string.Empty;
    public string PayPalVaultId { get; private set; } = string.Empty;
    public string PayPalCustomerId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string LastFour { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    public void MarkDeleted() => DeletedAt ??= DateTimeOffset.UtcNow;
}
