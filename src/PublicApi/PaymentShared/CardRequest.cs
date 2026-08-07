using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>
/// Raw card details a shopper supplies to pay a one-off order or to save a card. These are
/// forwarded straight to PayPal and are never stored in this app's database or written to logs.
/// </summary>
public class CardRequest
{
    /// <summary>Card number, 13-19 digits (e.g. the sandbox Visa 4111111111111111).</summary>
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in <c>YYYY-MM</c> form, e.g. <c>2030-01</c>.</summary>
    [Required]
    public string Expiry { get; set; } = string.Empty;

    /// <summary>3-4 digit card security code (CVV).</summary>
    [Required]
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Cardholder name as it appears on the card.</summary>
    public string? Name { get; set; }

    // Optional billing address.
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }

    /// <summary>Two-letter ISO country code for the billing address, e.g. <c>US</c>.</summary>
    public string? BillingCountryCode { get; set; }

    public CardDetails ToCardDetails() => new()
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        Name = Name,
        BillingAddressLine1 = BillingAddressLine1,
        BillingAddressLine2 = BillingAddressLine2,
        BillingAdminArea2 = BillingCity,
        BillingAdminArea1 = BillingState,
        BillingPostalCode = BillingPostalCode,
        BillingCountryCode = BillingCountryCode
    };
}
