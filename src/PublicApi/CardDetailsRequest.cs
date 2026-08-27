using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Card details supplied for a one-off payment or for saving a card.
/// These are passed straight through to PayPal and are never stored or logged.
/// </summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public PayPalCardDetails ToPayPalCardDetails()
    {
        return new PayPalCardDetails(
            Number?.Replace(" ", string.Empty) ?? string.Empty,
            ExpiryMonth,
            ExpiryYear,
            SecurityCode,
            Name,
            BillingAddress is null ? null : new PayPalBillingAddress(
                BillingAddress.AddressLine1,
                BillingAddress.City,
                BillingAddress.State,
                BillingAddress.PostalCode,
                BillingAddress.CountryCode ?? "US"));
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Number)) return "Card number is required.";
        if (ExpiryMonth is < 1 or > 12) return "Expiry month must be between 1 and 12.";
        if (ExpiryYear < 2000) return "A four-digit expiry year is required.";
        return null;
    }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
