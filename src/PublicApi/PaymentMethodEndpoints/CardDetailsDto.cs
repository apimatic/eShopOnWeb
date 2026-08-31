using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Full card details used only in transit to PayPal. They are never persisted
/// by this application and never written to logs.
/// </summary>
public class CardDetailsDto
{
    [Required]
    [RegularExpression("^[0-9]{13,19}$", ErrorMessage = "Card number must be 13-19 digits.")]
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM format.</summary>
    [Required]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$", ErrorMessage = "Expiry must be in YYYY-MM format.")]
    public string Expiry { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[0-9]{3,4}$", ErrorMessage = "Security code must be 3-4 digits.")]
    public string SecurityCode { get; set; } = string.Empty;

    public string? Name { get; set; }

    public BillingAddressDto? BillingAddress { get; set; }

    public GatewayCardDetails ToGatewayCard()
    {
        return new GatewayCardDetails(
            Number,
            Expiry,
            SecurityCode,
            Name,
            BillingAddress == null
                ? null
                : new GatewayAddress(
                    BillingAddress.AddressLine1,
                    BillingAddress.AddressLine2,
                    BillingAddress.City,
                    BillingAddress.State,
                    BillingAddress.PostalCode,
                    BillingAddress.CountryCode));
    }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    [Required]
    [RegularExpression("^([A-Z]{2}|C2)$", ErrorMessage = "Country code must be a two-letter ISO-3166-1 code.")]
    public string CountryCode { get; set; } = string.Empty;
}
