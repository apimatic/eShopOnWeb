namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied for a one-off card payment or to vault a card. These values
/// are passed straight to PayPal and are never persisted in the application's database
/// nor written to logs.
/// </summary>
public record CardPaymentDetails(
    string Number,
    string Expiry,          // YYYY-MM (RFC 3339 year-month)
    string SecurityCode,
    string? Name = null,
    CardBillingAddress? BillingAddress = null);

/// <summary>A billing address for a card. Country code is required by PayPal.</summary>
public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea1 = null,   // state / province
    string? AdminArea2 = null,   // city
    string? PostalCode = null);
