using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card a shopper has saved for reuse. The actual card data lives in a PCI-compliant
/// vault (PayPal), never in this database - so this entity only holds the vault token
/// plus a safe, non-sensitive description (brand, last four digits, expiry) that lets the
/// shopper recognise which card it is.
/// </summary>
public class PaymentMethod : BaseEntity
{
    /// <summary>Human-friendly label, e.g. "VISA ending 1111".</summary>
    public string? Alias { get; private set; }

    /// <summary>PayPal vault payment-token id. Used to charge the card later; never a PAN.</summary>
    public string? CardId { get; private set; }

    /// <summary>Last four digits of the card, safe to display.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Card brand as reported by PayPal (VISA, MASTERCARD, ...).</summary>
    public string? Brand { get; private set; }

    /// <summary>Card expiry in "YYYY-MM" form, safe to display.</summary>
    public string? Expiry { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string cardId, string last4, string brand, string expiry, string? alias = null)
    {
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));

        CardId = cardId;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        Alias = alias ?? BuildAlias(brand, last4);
    }

    private static string BuildAlias(string? brand, string? last4)
    {
        var brandText = string.IsNullOrWhiteSpace(brand) ? "Card" : brand;
        return string.IsNullOrWhiteSpace(last4) ? brandText : $"{brandText} ending {last4}";
    }
}
