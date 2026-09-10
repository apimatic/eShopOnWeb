namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card data supplied by a shopper for a one-off payment or to be vaulted.
/// These values are passed straight through to PayPal and are never persisted or logged by this app.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

/// <summary>
/// The portable billing address for a card, as PayPal expects it.
/// </summary>
public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode);
