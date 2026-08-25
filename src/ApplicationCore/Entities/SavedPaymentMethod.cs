using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private SavedPaymentMethod() { }
#pragma warning restore CS8618

    public SavedPaymentMethod(string buyerId, string paymentTokenId, string? payPalCustomerId, string? last4, string? brand, string? expiry)
    {
        BuyerId = buyerId;
        PaymentTokenId = paymentTokenId;
        PayPalCustomerId = payPalCustomerId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
    }

    public string BuyerId { get; private set; }
    public string PaymentTokenId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; }
}
