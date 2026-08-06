using System.Globalization;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied for a one-off payment or to vault a card. This is a transient carrier
/// used only to reach the payment provider — it is never persisted to the application database and
/// never written to logs.
/// </summary>
public sealed class CardDetails
{
    public CardDetails(string number, int expiryMonth, int expiryYear, string securityCode,
        string cardholderName, BillingAddress? billingAddress = null)
    {
        Number = Guard.Against.NullOrWhiteSpace(number, nameof(number)).Replace(" ", string.Empty);
        ExpiryMonth = Guard.Against.OutOfRange(expiryMonth, nameof(expiryMonth), 1, 12);
        // Four-digit year; guarded to a sane range so a malformed year can't produce a bad wire value.
        ExpiryYear = Guard.Against.OutOfRange(expiryYear, nameof(expiryYear), 2000, 2099);
        SecurityCode = Guard.Against.NullOrWhiteSpace(securityCode, nameof(securityCode));
        CardholderName = Guard.Against.NullOrWhiteSpace(cardholderName, nameof(cardholderName));
        BillingAddress = billingAddress;
    }

    public string Number { get; }
    public int ExpiryMonth { get; }
    public int ExpiryYear { get; }
    public string SecurityCode { get; }
    public string CardholderName { get; }
    public BillingAddress? BillingAddress { get; }

    /// <summary>Expiry in PayPal's "YYYY-MM" wire form.</summary>
    public string ExpiryWireValue =>
        string.Create(CultureInfo.InvariantCulture, $"{ExpiryYear:D4}-{ExpiryMonth:D2}");
}

/// <summary>
/// Optional billing address for a card. PayPal requires only the two-letter country code; the rest
/// is optional and defaults are supplied by the caller.
/// </summary>
public sealed class BillingAddress
{
    public BillingAddress(string countryCode, string? addressLine1 = null, string? addressLine2 = null,
        string? adminArea1 = null, string? adminArea2 = null, string? postalCode = null)
    {
        CountryCode = Guard.Against.NullOrWhiteSpace(countryCode, nameof(countryCode));
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        AdminArea1 = adminArea1;
        AdminArea2 = adminArea2;
        PostalCode = postalCode;
    }

    /// <summary>ISO 3166-1 alpha-2 country code, e.g. "US".</summary>
    public string CountryCode { get; }
    public string? AddressLine1 { get; }
    public string? AddressLine2 { get; }
    /// <summary>State / province (PayPal admin_area_1).</summary>
    public string? AdminArea1 { get; }
    /// <summary>City / town (PayPal admin_area_2).</summary>
    public string? AdminArea2 { get; }
    public string? PostalCode { get; }
}
