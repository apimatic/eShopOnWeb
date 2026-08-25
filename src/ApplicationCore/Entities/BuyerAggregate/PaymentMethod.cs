using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card saved for reuse. The raw card number is never stored here — only PayPal's vault
/// token id (<see cref="CardId"/>) and a safe descriptor (brand/last 4/expiry) to show the shopper.
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() {}

    public PaymentMethod(string cardId, string brand, string last4, string expiry)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        CardId = cardId; // PayPal vault payment token id -- not a PCI card number
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = $"{brand} ••••{last4}";
        IsActive = true;
    }

    public string? Alias { get; private set; }
    public string? CardId { get; private set; } // PayPal vault payment token id
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }
    public string? Expiry { get; private set; } // "YYYY-MM"
    public bool IsActive { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
    }
}
