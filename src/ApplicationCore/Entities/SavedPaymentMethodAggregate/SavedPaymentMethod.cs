using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalVaultTokenId,
        string? cardBrand, string? last4, string? cardExpiry, string? cardholderName)
    {
        BuyerId = buyerId;
        PayPalVaultTokenId = payPalVaultTokenId;
        CardBrand = cardBrand;
        Last4 = last4;
        CardExpiry = cardExpiry;
        CardholderName = cardholderName;
        CreatedAt = DateTimeOffset.UtcNow;
        IsDeleted = false;
    }

    public string BuyerId { get; private set; }
    public string PayPalVaultTokenId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? Last4 { get; private set; }
    public string? CardExpiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public void SoftDelete() => IsDeleted = true;
}
