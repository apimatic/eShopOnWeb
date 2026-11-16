using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A card saved at Finix. Only non-sensitive display data is stored here; the PAN/CVV never
/// reach this application's database.
/// </summary>
public class SavedCard
{
    private SavedCard() { }

    public SavedCard(string paymentMethodId, string buyerId, string finixIdentityId,
        string? last4, string? brand, int? expirationMonth, int? expirationYear)
    {
        PaymentMethodId = paymentMethodId;
        BuyerId = buyerId;
        FinixIdentityId = finixIdentityId;
        Last4 = last4;
        Brand = brand;
        ExpirationMonth = expirationMonth;
        ExpirationYear = expirationYear;
        Enabled = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PaymentMethodId { get; private set; }
    public string BuyerId { get; private set; }
    public string FinixIdentityId { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public int? ExpirationMonth { get; private set; }
    public int? ExpirationYear { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Disable() => Enabled = false;

    public void Enable() => Enabled = true;
}
