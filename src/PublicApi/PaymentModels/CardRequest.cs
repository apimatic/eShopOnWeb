using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied by a shopper for a one-off payment or to be saved. These values are
/// forwarded to PayPal and are never persisted in the application database or written to logs.
/// </summary>
public class CardRequest
{
    /// <summary>Card number, e.g. the sandbox Visa test card 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form, e.g. 2027-02.</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card security code (CVC). Optional but recommended for one-off payments.</summary>
    public string? SecurityCode { get; set; }

    public string CardholderName { get; set; } = string.Empty;

    public BillingAddressRequest? BillingAddress { get; set; }

    /// <summary>Validates required fields and returns a human-readable reason when invalid.</summary>
    public bool TryValidate(out string error)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Number)) missing.Add("number");
        if (string.IsNullOrWhiteSpace(Expiry)) missing.Add("expiry");
        if (string.IsNullOrWhiteSpace(CardholderName)) missing.Add("cardholderName");
        if (BillingAddress is null)
        {
            missing.Add("billingAddress");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(BillingAddress.AddressLine1)) missing.Add("billingAddress.addressLine1");
            if (string.IsNullOrWhiteSpace(BillingAddress.City)) missing.Add("billingAddress.city");
            if (string.IsNullOrWhiteSpace(BillingAddress.PostalCode)) missing.Add("billingAddress.postalCode");
            if (string.IsNullOrWhiteSpace(BillingAddress.CountryCode)) missing.Add("billingAddress.countryCode");
        }

        if (missing.Count > 0)
        {
            error = "Missing required card fields: " + string.Join(", ", missing) + ".";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public CardDetails ToCardDetails() => new(
        Number: Number.Replace(" ", string.Empty),
        Expiry: Expiry.Trim(),
        SecurityCode: string.IsNullOrWhiteSpace(SecurityCode) ? null : SecurityCode.Trim(),
        CardholderName: CardholderName.Trim(),
        BillingAddress: new CardBillingAddress(
            AddressLine1: BillingAddress!.AddressLine1.Trim(),
            AddressLine2: string.IsNullOrWhiteSpace(BillingAddress.AddressLine2) ? null : BillingAddress.AddressLine2!.Trim(),
            City: BillingAddress.City.Trim(),
            State: string.IsNullOrWhiteSpace(BillingAddress.State) ? null : BillingAddress.State!.Trim(),
            PostalCode: BillingAddress.PostalCode.Trim(),
            CountryCode: BillingAddress.CountryCode.Trim()));
}

public class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;

    /// <summary>State/province code, e.g. "CA". Optional.</summary>
    public string? State { get; set; }
    public string PostalCode { get; set; } = string.Empty;

    /// <summary>Two-letter country code, e.g. "US".</summary>
    public string CountryCode { get; set; } = string.Empty;
}
