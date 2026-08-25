using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string vaultTokenId, string? last4, string? cardBrand, string? payPalCustomerId)
    {
        BuyerId = buyerId;
        VaultTokenId = vaultTokenId;
        Last4 = last4;
        CardBrand = cardBrand;
        PayPalCustomerId = payPalCustomerId;
    }

    public string BuyerId { get; private set; }
    public string VaultTokenId { get; private set; }
    public string? Last4 { get; private set; }
    public string? CardBrand { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}
