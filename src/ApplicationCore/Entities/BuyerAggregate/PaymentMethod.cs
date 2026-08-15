using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. Full card details are never stored here — only the PayPal
/// vault token that stands in for the card, plus a safe descriptor (brand + last four digits + expiry)
/// so the shopper can recognise which card it is.
/// </summary>
public class PaymentMethod : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618

    public PaymentMethod(string vaultId, string brand, string last4, string? expiry, string? cardholderName, string? alias)
    {
        VaultId = vaultId;
        Brand = brand;
        Last4 = last4;
        Expiry = expiry;
        CardholderName = cardholderName;
        Alias = alias;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Optional shopper-supplied label for the card.</summary>
    public string? Alias { get; private set; }

    /// <summary>The PayPal-generated vault id that represents the saved card. This is NOT the card number.</summary>
    public string VaultId { get; private set; }

    /// <summary>Card network brand (e.g. VISA), as reported by PayPal. Safe to show.</summary>
    public string Brand { get; private set; }

    /// <summary>Last digits of the card, as reported by PayPal. Safe to show.</summary>
    public string Last4 { get; private set; }

    /// <summary>Card expiry in YYYY-MM form, as reported by PayPal. Safe to show.</summary>
    public string? Expiry { get; private set; }

    public string? CardholderName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
