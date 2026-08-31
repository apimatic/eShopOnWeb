using System;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.PublicApi.Models;

/// <summary>
/// Full card details supplied by the caller. Held only in memory for the
/// duration of the request; never persisted and never logged.
/// </summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }

    public PayPalCardDetails ToPayPalCardDetails()
    {
        var digits = new string((Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 12 || digits.Length > 19)
        {
            throw new ArgumentException("The card number is invalid.");
        }
        if (ExpiryMonth < 1 || ExpiryMonth > 12)
        {
            throw new ArgumentException("The card expiry month must be between 1 and 12.");
        }
        var year = ExpiryYear < 100 ? 2000 + ExpiryYear : ExpiryYear;
        var expiry = new DateTime(year, ExpiryMonth, 1).AddMonths(1);
        if (expiry <= DateTime.UtcNow)
        {
            throw new ArgumentException("The card expiry date must be in the future.");
        }

        return new PayPalCardDetails(
            digits,
            $"{year:D4}-{ExpiryMonth:D2}",
            SecurityCode,
            Name,
            BillingAddress?.ToPayPalBillingAddress());
    }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";

    public PayPalBillingAddress ToPayPalBillingAddress()
    {
        if (string.IsNullOrWhiteSpace(CountryCode) || CountryCode.Length != 2)
        {
            throw new ArgumentException("The billing address country code must be a 2-letter ISO code.");
        }
        return new PayPalBillingAddress(AddressLine1, State, City, PostalCode, CountryCode.ToUpperInvariant());
    }
}
