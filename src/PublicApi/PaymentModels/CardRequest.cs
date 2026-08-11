using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied by the shopper for a one-off payment or to be saved. These are forwarded to PayPal
/// and never stored in the application database or written to logs.
/// </summary>
public class CardRequest
{
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM format (as PayPal requires).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;

    // Optional billing address (recommended for card verification).
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardDetails ToCardDetails()
    {
        var hasBilling = AddressLine1 is not null || AddressLine2 is not null || City is not null
            || State is not null || PostalCode is not null || CountryCode is not null;

        var billing = hasBilling
            ? new BillingAddress(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode)
            : null;

        return new CardDetails(CardNumber, Expiry, SecurityCode, CardholderName, billing);
    }
}
