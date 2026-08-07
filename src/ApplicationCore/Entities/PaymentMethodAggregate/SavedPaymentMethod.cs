using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

/// <summary>
/// A card a shopper has vaulted with PayPal for reuse on later orders.
///
/// This entity NEVER holds full card details. The only reference to the card is
/// <see cref="VaultId"/> — an opaque PayPal vault token — plus a safe descriptor
/// (brand / last four digits / expiry) so the shopper can recognise which card it is.
/// The PAN and CVC live only inside PayPal's vault.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>Identity of the shopper who owns this card. A card belongs only to its owner.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Opaque PayPal vault token used to charge this card. Not card data.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network (e.g. VISA), for display only.</summary>
    public string CardBrand { get; private set; }

    /// <summary>Last four digits of the PAN, for display only.</summary>
    public string LastFourDigits { get; private set; }

    /// <summary>Card expiry as reported by PayPal (YYYY-MM), for display only.</summary>
    public string Expiry { get; private set; }

    /// <summary>Optional shopper-supplied label to tell cards apart.</summary>
    public string? Alias { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string vaultId, string cardBrand, string lastFourDigits, string expiry, string? alias = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(vaultId, nameof(vaultId));
        Guard.Against.NullOrEmpty(cardBrand, nameof(cardBrand));
        Guard.Against.NullOrEmpty(lastFourDigits, nameof(lastFourDigits));
        Guard.Against.NullOrEmpty(expiry, nameof(expiry));

        BuyerId = buyerId;
        VaultId = vaultId;
        CardBrand = cardBrand;
        LastFourDigits = lastFourDigits;
        Expiry = expiry;
        Alias = alias;
    }
}
