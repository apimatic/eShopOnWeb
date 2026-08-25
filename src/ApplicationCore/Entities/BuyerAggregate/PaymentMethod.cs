using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

public class PaymentMethod : BaseEntity
{
    public string? Alias { get; private set; }
    public string CardId { get; private set; } // PayPal-generated vault payment token id (PCI-safe reference; never the raw card number)
    public string Last4 { get; private set; }
    public string Brand { get; private set; }
    public string Expiry { get; private set; } // YYYY-MM

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string cardId, string brand, string last4, string expiry, string? alias = null)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        CardId = cardId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
    }
}
