using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Raw card details supplied for a one-off payment or to save a card. Carried only in the request
/// and passed straight to PayPal; never persisted in the application database and never logged.
/// </summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry as "YYYY-MM" or "MM/YY".</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public PayPalCardDetails ToDetails() => new(
        Number, Expiry, SecurityCode, Name,
        BillingAddressLine1, BillingAddressLine2, BillingCity, BillingState, BillingPostalCode, BillingCountryCode);
}
