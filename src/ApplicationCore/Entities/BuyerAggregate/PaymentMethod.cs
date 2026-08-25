using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

// A shopper's saved card. Only a PayPal vault token id and safe-to-display details are stored here —
// the actual card number is never persisted by this application, only by PayPal.
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() {}

    public PaymentMethod(int buyerId, string vaultId, string? brand, string? last4, int? expiryMonth, int? expiryYear, DateTimeOffset createdAt)
    {
        BuyerId = buyerId;
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        ExpiryMonth = expiryMonth;
        ExpiryYear = expiryYear;
        CreatedAt = createdAt;
    }

    public int BuyerId { get; private set; }

    // The PayPal vault payment-token id. Used to authorize future orders without resubmitting the card.
    public string VaultId { get; private set; }
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public int? ExpiryMonth { get; private set; }
    public int? ExpiryYear { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
