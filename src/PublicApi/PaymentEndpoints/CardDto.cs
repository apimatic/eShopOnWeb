using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Card details supplied by a caller for a one-off payment or to save a card. These are
/// forwarded straight to PayPal and never persisted or logged by this application.
/// </summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form (PayPal's format).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }

    public string CardholderName { get; set; } = string.Empty;

    public string? BillingLine1 { get; set; }
    public string? BillingLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }

    public PaymentCard ToPaymentCard() => new(
        Number: Number,
        Expiry: Expiry,
        SecurityCode: SecurityCode,
        CardholderName: CardholderName,
        BillingAddress: new CardBillingAddress(
            Line1: BillingLine1,
            Line2: BillingLine2,
            City: BillingCity,
            State: BillingState,
            PostalCode: BillingPostalCode,
            CountryCode: BillingCountryCode));
}
