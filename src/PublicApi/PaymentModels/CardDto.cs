using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied by the caller for a one-off payment or to save a card. Carried only to
/// reach the payment provider; never persisted or logged by the application.
/// </summary>
public class CardDto
{
    /// <summary>Card number, e.g. the sandbox Visa 4111111111111111. Spaces are ignored.</summary>
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry month, 1-12.</summary>
    [Range(1, 12)]
    public int ExpiryMonth { get; set; }

    /// <summary>Four-digit expiry year, e.g. 2027.</summary>
    [Range(2000, 2099)]
    public int ExpiryYear { get; set; }

    /// <summary>Card security code (CVC).</summary>
    [Required]
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Cardholder name.</summary>
    [Required]
    public string CardholderName { get; set; } = string.Empty;

    // Optional billing address. PayPal requires only a country code; the rest is optional.
    public string? BillingCountryCode { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }

    /// <summary>Maps this transport DTO onto the domain <see cref="CardDetails"/>.</summary>
    public CardDetails ToCardDetails()
    {
        BillingAddress? billing = null;
        if (!string.IsNullOrWhiteSpace(BillingCountryCode)
            || !string.IsNullOrWhiteSpace(BillingAddressLine1)
            || !string.IsNullOrWhiteSpace(BillingCity)
            || !string.IsNullOrWhiteSpace(BillingState)
            || !string.IsNullOrWhiteSpace(BillingPostalCode))
        {
            billing = new BillingAddress(
                countryCode: string.IsNullOrWhiteSpace(BillingCountryCode) ? "US" : BillingCountryCode!,
                addressLine1: BillingAddressLine1,
                addressLine2: BillingAddressLine2,
                adminArea2: BillingCity,
                adminArea1: BillingState,
                postalCode: BillingPostalCode);
        }

        return new CardDetails(Number, ExpiryMonth, ExpiryYear, SecurityCode, CardholderName, billing);
    }
}
