using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card input for a one-off payment or to be saved. These values are used only to build the
/// PayPal request; they are never stored in this app's database and never logged.
/// </summary>
public class CardInput
{
    public string? Number { get; set; }
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressInput? BillingAddress { get; set; }

    /// <summary>Validate and project onto the domain <see cref="CardDetails"/>.</summary>
    public CardDetails ToCardDetails()
    {
        if (string.IsNullOrWhiteSpace(Number))
        {
            throw new OrderRequestInvalidException("Card number is required.");
        }
        if (ExpiryMonth is < 1 or > 12)
        {
            throw new OrderRequestInvalidException("Card expiry month must be between 1 and 12.");
        }
        if (ExpiryYear < 2000 || ExpiryYear > 2100)
        {
            throw new OrderRequestInvalidException("Card expiry year is invalid.");
        }
        if (string.IsNullOrWhiteSpace(SecurityCode))
        {
            throw new OrderRequestInvalidException("Card security code is required.");
        }
        if (string.IsNullOrWhiteSpace(CardholderName))
        {
            throw new OrderRequestInvalidException("Cardholder name is required.");
        }

        var expiry = string.Create(CultureInfo.InvariantCulture, $"{ExpiryYear:D4}-{ExpiryMonth:D2}");
        return new CardDetails(
            Number.Replace(" ", string.Empty),
            expiry,
            SecurityCode,
            CardholderName,
            BillingAddress?.ToBillingAddress());
    }
}

public class BillingAddressInput
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public BillingAddress ToBillingAddress()
    {
        if (string.IsNullOrWhiteSpace(CountryCode))
        {
            throw new OrderRequestInvalidException("Billing address country code is required when a billing address is supplied.");
        }
        return new BillingAddress(
            AddressLine1 ?? string.Empty,
            AddressLine2,
            City ?? string.Empty,
            State,
            PostalCode ?? string.Empty,
            CountryCode);
    }
}
