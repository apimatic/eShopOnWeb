using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Raw card details for a one-off payment or for saving a card. They are forwarded to PayPal
/// within the request and are never persisted or logged by this application.
/// </summary>
public class CardDetailsDto
{
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM format.</summary>
    [Required]
    public string Expiry { get; set; } = string.Empty;

    [Required]
    public string SecurityCode { get; set; } = string.Empty;

    public string HolderName { get; set; } = string.Empty;

    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(
        Number,
        Expiry,
        SecurityCode,
        HolderName,
        BillingAddress is null
            ? null
            : new BillingAddress(
                BillingAddress.AddressLine1,
                BillingAddress.AddressLine2,
                BillingAddress.City,
                BillingAddress.State,
                BillingAddress.PostalCode,
                BillingAddress.CountryCode));
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}
