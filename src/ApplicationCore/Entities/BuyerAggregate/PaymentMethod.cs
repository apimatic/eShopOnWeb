using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. Full card details are never stored here — only PayPal's
/// vault token id (which stands in for the card) plus a safe descriptor (brand, last four digits,
/// expiry) so the shopper can recognise which card it is.
/// </summary>
public class PaymentMethod : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string vaultId, string? brand, string? last4, string? expiry, string? alias)
    {
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>An optional shopper-friendly label.</summary>
    public string? Alias { get; private set; }

    /// <summary>
    /// The PayPal-generated vault token id for the saved card. The actual card data lives only in
    /// PayPal's PCI-compliant vault; this app stores only the token that references it.
    /// </summary>
    public string VaultId { get; private set; }

    /// <summary>Card network (e.g. VISA), for safe display.</summary>
    public string? Brand { get; private set; }

    /// <summary>The last four digits of the card, for safe display.</summary>
    public string? Last4 { get; private set; }

    /// <summary>The card expiry (YYYY-MM), for safe display.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
