using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper has saved for reuse. No full card details are ever stored here:
/// the card lives in PayPal's vault and is referenced only by its vault payment-token id
/// (<see cref="PayPalTokenId"/>). The remaining fields are a PCI-safe descriptor
/// (brand, last four digits, expiry) so the shopper can recognise which card it is.
/// </summary>
public class PaymentMethod : BaseEntity
{
    /// <summary>Optional shopper-supplied label, e.g. "Personal Visa".</summary>
    public string? Alias { get; private set; }

    /// <summary>PayPal vault payment-token id used to charge the saved card. Never the card number.</summary>
    public string? PayPalTokenId { get; private set; }

    /// <summary>Card brand for safe display, e.g. "VISA".</summary>
    public string? CardBrand { get; private set; }

    /// <summary>Last four digits of the card, for safe display.</summary>
    public string? Last4 { get; private set; }

    /// <summary>Card expiry for safe display, in PayPal's "YYYY-MM" format.</summary>
    public string? Expiry { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }

    public PaymentMethod(string? alias, string payPalTokenId, string? cardBrand, string? last4, string? expiry)
    {
        Guard.Against.NullOrEmpty(payPalTokenId, nameof(payPalTokenId));

        Alias = alias;
        PayPalTokenId = payPalTokenId;
        CardBrand = cardBrand;
        Last4 = last4;
        Expiry = expiry;
    }
}
