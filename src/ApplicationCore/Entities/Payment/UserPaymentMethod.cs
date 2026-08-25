using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

public class UserPaymentMethod : BaseEntity, IAggregateRoot
{
    private UserPaymentMethod() { }

    public UserPaymentMethod(string userId, string paypalCustomerId, string paymentTokenId, string last4, string brand, string expiry)
    {
        Guard.Against.NullOrEmpty(userId, nameof(userId));
        Guard.Against.NullOrEmpty(paymentTokenId, nameof(paymentTokenId));

        UserId = userId;
        PayPalCustomerId = paypalCustomerId;
        PaymentTokenId = paymentTokenId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
    }

    public string UserId { get; private set; } = string.Empty;
    public string PayPalCustomerId { get; private set; } = string.Empty;
    public string PaymentTokenId { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Expiry { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }

    public void MarkDeleted() => IsDeleted = true;
}
