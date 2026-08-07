using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The application NEVER stores the full card details:
/// the real card lives in PayPal's PCI-compliant vault and is referenced here only by its
/// PayPal payment-token id (<see cref="VaultId"/>). The remaining fields are the safe display
/// summary (brand, last four digits, expiry) so a shopper can recognise which card this is.
/// </summary>
public class PaymentMethod : BaseEntity
{
    /// <summary>Friendly, shopper-supplied label (optional), e.g. "Personal Visa".</summary>
    public string? Alias { get; private set; }

    /// <summary>
    /// The PayPal payment-token id (vault id) used to charge this card. This is a tokenised
    /// reference held in PayPal's PCI-compliant vault — not the card number itself.
    /// </summary>
    public string VaultId { get; private set; }

    /// <summary>Card brand as reported by PayPal, e.g. VISA.</summary>
    public string CardBrand { get; private set; }

    /// <summary>Last four digits of the card, safe to display.</summary>
    public string Last4 { get; private set; }

    /// <summary>Expiry in PayPal's "YYYY-MM" format, safe to display.</summary>
    public string Expiry { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string vaultId, string cardBrand, string last4, string expiry, string? alias)
    {
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(last4, nameof(last4));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        VaultId = vaultId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
    }
}
