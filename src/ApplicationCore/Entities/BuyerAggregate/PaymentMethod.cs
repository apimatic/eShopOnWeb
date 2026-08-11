using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A saved card belonging to a <see cref="Buyer"/>. The actual card data lives in PayPal's
/// PCI-compliant vault; this app stores only the PayPal vault token id and a safe descriptor
/// (brand, last four, expiry, alias) so the shopper can recognise which card it is. Full card
/// details are never stored here.
/// </summary>
public class PaymentMethod : BaseEntity
{
    public string? Alias { get; private set; }

    /// <summary>The PayPal-generated vault token id used to charge this card later.</summary>
    public string? CardId { get; private set; }

    public string? Last4 { get; private set; }

    /// <summary>The card brand/network reported by PayPal (e.g. VISA), for display only.</summary>
    public string? Brand { get; private set; }

    /// <summary>The card expiry as reported by PayPal, in YYYY-MM form, for display only.</summary>
    public string? Expiry { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string alias, string vaultTokenId, string? last4, string? brand, string? expiry)
    {
        Alias = alias;
        CardId = vaultTokenId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
    }
}
