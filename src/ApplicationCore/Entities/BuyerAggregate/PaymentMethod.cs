using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper saved for reuse. The application never stores full card details: the actual
/// card lives in PayPal's PCI-compliant Vault and is referenced here only by its vault token
/// (<see cref="CardId"/>). The remaining fields are the safe, display-only descriptors the shopper
/// uses to recognise which card this is.
/// </summary>
public class PaymentMethod : BaseEntity
{
    public string Alias { get; private set; }

    /// <summary>PayPal Vault payment-token id. This is a reference, never the card number.</summary>
    public string CardId { get; private set; }

    public string Last4 { get; private set; }
    public string Brand { get; private set; }

    /// <summary>Card expiry in PayPal's "YYYY-MM" form; safe to display.</summary>
    public string Expiry { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string alias, string cardId, string last4, string brand, string expiry)
    {
        Alias = Guard.Against.NullOrEmpty(alias, nameof(alias));
        CardId = Guard.Against.NullOrEmpty(cardId, nameof(cardId));
        Last4 = Guard.Against.NullOrEmpty(last4, nameof(last4));
        Brand = Guard.Against.NullOrEmpty(brand, nameof(brand));
        Expiry = Guard.Against.NullOrEmpty(expiry, nameof(expiry));
    }
}
