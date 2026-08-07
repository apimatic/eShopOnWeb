using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper has saved for later reuse. Only PCI-safe descriptive data lives here plus the
/// PayPal vault token id (<see cref="VaultId"/>) that stands in for the real card, which is stored by
/// PayPal - never in this application's database and never logged.
/// </summary>
public class PaymentMethod : BaseEntity
{
    /// <summary>PayPal Payment Method Token (vault) id used to charge the card. This is the only handle
    /// we keep to the underlying card; the card data itself is held by PayPal.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network, e.g. VISA - safe to display.</summary>
    public string Brand { get; private set; }

    /// <summary>Last four digits - safe to display so the shopper can recognise the card.</summary>
    public string Last4 { get; private set; }

    /// <summary>Expiry in YYYY-MM form - safe to display.</summary>
    public string Expiry { get; private set; }

    /// <summary>Optional shopper-supplied nickname for the card.</summary>
    public string? Alias { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string vaultId, string brand, string last4, string expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(brand, nameof(brand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
    }
}
