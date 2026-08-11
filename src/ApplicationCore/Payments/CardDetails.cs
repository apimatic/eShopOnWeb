namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card data for a one-off payment or to be vaulted. This is a transient carrier only — it is
/// passed straight to PayPal and is never persisted in the application database nor written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string Name,
    CardBillingAddress? BillingAddress);

/// <summary>Billing address for a card, using PayPal's address field names.</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state / province
    string? PostalCode,
    string? CountryCode);
