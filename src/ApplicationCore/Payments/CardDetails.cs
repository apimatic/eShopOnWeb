namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied for a one-off payment or to be vaulted. These are passed straight to
/// PayPal and are never persisted in this application's database or written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    BillingAddressDetails? BillingAddress);

/// <summary>
/// Portable billing address, mapped to PayPal's address model. All fields optional; PayPal uses it
/// for AVS / risk checks.
/// </summary>
public record BillingAddressDetails(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);
