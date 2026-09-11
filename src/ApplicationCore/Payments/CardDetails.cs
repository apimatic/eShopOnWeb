namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied by a shopper for a one-off payment or to be vaulted. This type is
/// only ever passed through to the PayPal gateway; the number and security code are never
/// persisted in the application's own database and never written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea2 = null,
    string? AdminArea1 = null,
    string? PostalCode = null,
    string? CountryCode = null);
