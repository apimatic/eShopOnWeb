namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The actual card data lives in PayPal's vault (a PCI
/// compliant system); this app keeps only PayPal's vault token id plus the safe display fields
/// (brand, last four, expiry) so a shopper can recognise which card it is. Full card details are
/// never stored here.
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string vaultTokenId, string? alias, string? brand, string? last4, string? expiry)
    {
        VaultTokenId = vaultTokenId;
        Alias = alias;
        CardBrand = brand;
        Last4 = last4;
        Expiry = expiry;
    }

    /// <summary>Shopper-friendly label for the saved card.</summary>
    public string? Alias { get; private set; }

    /// <summary>PayPal vault payment-token id. This — never a card number — is what pays a later order.</summary>
    public string? VaultTokenId { get; private set; }

    public string? CardBrand { get; private set; }

    public string? Last4 { get; private set; }

    /// <summary>Card expiry as returned by PayPal (YYYY-MM); safe to show, not sensitive.</summary>
    public string? Expiry { get; private set; }
}
