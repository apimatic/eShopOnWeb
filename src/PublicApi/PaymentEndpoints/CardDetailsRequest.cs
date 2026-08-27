using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Card details supplied for a one-off payment or for saving a card.
/// Processed in memory only: never persisted, never logged.
/// </summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string BillingCountryCode { get; set; } = "US";

    public CardDetails ToCardDetails() => new CardDetails
    {
        Number = Number,
        Expiry = Expiry,
        SecurityCode = SecurityCode,
        CardholderName = CardholderName,
        BillingAddressLine1 = BillingAddressLine1,
        BillingAddressLine2 = BillingAddressLine2,
        BillingCity = BillingCity,
        BillingState = BillingState,
        BillingPostalCode = BillingPostalCode,
        BillingCountryCode = BillingCountryCode
    };

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Number) || Number.Length < 13 || Number.Length > 19)
            return "card.number must be 13-19 digits.";
        if (string.IsNullOrWhiteSpace(Expiry) || !System.Text.RegularExpressions.Regex.IsMatch(Expiry, @"^\d{4}-\d{2}$"))
            return "card.expiry must be in YYYY-MM format.";
        if (string.IsNullOrWhiteSpace(SecurityCode) || SecurityCode.Length < 3 || SecurityCode.Length > 4)
            return "card.securityCode must be 3-4 digits.";
        return null;
    }
}
