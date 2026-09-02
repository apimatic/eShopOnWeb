using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Full card details for a one-off payment or for saving a card.
/// These details are forwarded to PayPal and are never persisted or logged by this app.
/// </summary>
public class CardDetailsRequest
{
    [Required]
    [RegularExpression("^[0-9]{13,19}$", ErrorMessage = "Card number must be 13-19 digits.")]
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM format.</summary>
    [Required]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$", ErrorMessage = "Expiry must be in YYYY-MM format.")]
    public string Expiry { get; set; } = string.Empty;

    [RegularExpression("^[0-9]{3,4}$", ErrorMessage = "Security code must be 3-4 digits.")]
    public string? SecurityCode { get; set; }

    public string? CardholderName { get; set; }

    public CardBillingAddressRequest? BillingAddress { get; set; }

    public CardDetails ToCardDetails()
    {
        return new CardDetails
        {
            Number = Number,
            Expiry = Expiry,
            SecurityCode = SecurityCode,
            CardholderName = CardholderName,
            BillingAddress = BillingAddress is null ? null : new CardBillingAddress
            {
                AddressLine1 = BillingAddress.AddressLine1,
                AddressLine2 = BillingAddress.AddressLine2,
                City = BillingAddress.City,
                State = BillingAddress.State,
                PostalCode = BillingAddress.PostalCode,
                CountryCode = BillingAddress.CountryCode ?? string.Empty
            }
        };
    }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    [Required]
    public string? CountryCode { get; set; }
}
