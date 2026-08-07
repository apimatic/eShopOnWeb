using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Raw card details accepted from a caller for a one-off payment or to be saved. These values are
/// used transiently to talk to PayPal and are never persisted in this app's database or logged.
/// </summary>
public class CardInputModel
{
    /// <summary>Card number (PAN). PayPal sandbox test card: 4111 1111 1111 1111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form, e.g. 2030-01.</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card security code (CVC).</summary>
    public string SecurityCode { get; set; } = string.Empty;

    public string? CardholderName { get; set; }

    public string? BillingCountryCode { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }

    public bool HasCardNumber => !string.IsNullOrWhiteSpace(Number)
        && !string.IsNullOrWhiteSpace(Expiry)
        && !string.IsNullOrWhiteSpace(SecurityCode);

    public CardDetails ToCardDetails() => new()
    {
        Number = Number.Replace(" ", string.Empty),
        Expiry = Expiry.Trim(),
        SecurityCode = SecurityCode.Trim(),
        CardholderName = CardholderName,
        BillingCountryCode = BillingCountryCode,
        BillingAddressLine1 = BillingAddressLine1,
        BillingAddressLine2 = BillingAddressLine2,
        BillingCity = BillingCity,
        BillingState = BillingState,
        BillingPostalCode = BillingPostalCode
    };

    /// <summary>Last four digits of the card number, for a display-safe descriptor / dedupe check.</summary>
    public string LastFour()
    {
        var digits = new string(Number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    /// <summary>
    /// A short, stable fingerprint of the card, used only to distinguish payment attempts when building
    /// an idempotency key. It is a one-way hash — the PAN itself never leaves this method.
    /// </summary>
    public string Fingerprint()
    {
        var material = $"{Number.Replace(" ", string.Empty)}|{Expiry.Trim()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return string.Concat(bytes.Take(8).Select(b => b.ToString("x2")));
    }
}
