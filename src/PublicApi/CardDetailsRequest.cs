using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Full card details, accepted only in transit and forwarded to PayPal.
/// Never persisted, never logged.
/// </summary>
public class CardDetailsRequest
{
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiration in YYYY-MM format, e.g. 2028-04.</summary>
    [Required]
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }

    public CardPaymentSource ToCardPaymentSource() =>
        new(Number, Expiry, SecurityCode, Name, BillingAddress?.ToCardBillingAddress());
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    [Required]
    public string CountryCode { get; set; } = string.Empty;

    public CardBillingAddress ToCardBillingAddress() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}
