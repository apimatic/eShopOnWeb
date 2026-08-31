using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Full card details, accepted only in transit and forwarded to PayPal.
/// Never persisted, never logged.
/// </summary>
public class CardDetailsDto
{
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>Format YYYY-MM.</summary>
    [Required]
    public string Expiry { get; set; } = string.Empty;

    [Required]
    public string SecurityCode { get; set; } = string.Empty;

    [Required]
    public string CardholderName { get; set; } = string.Empty;

    public CardBillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToModel() => new CardDetails(Number, Expiry, SecurityCode, CardholderName,
        BillingAddress is null ? null : new CardBillingAddress(BillingAddress.CountryCode, BillingAddress.AddressLine1,
            BillingAddress.AddressLine2, BillingAddress.City, BillingAddress.State, BillingAddress.PostalCode));
}

public class CardBillingAddressDto
{
    [Required]
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
}
