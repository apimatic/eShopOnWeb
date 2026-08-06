namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied by the shopper for a one-off payment or to be vaulted.
/// This object is transient: it is passed straight to the PayPal gateway and is NEVER
/// persisted in the application database and NEVER written to logs.
/// </summary>
public record CardPaymentDetails(
    string Number,
    string Expiry,        // "YYYY-MM" per PayPal card schema
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

/// <summary>Billing address for a card. Mirrors the PayPal card billing_address shape.</summary>
public record CardBillingAddress(
    string CountryCode,   // 2-letter ISO code, required by PayPal
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea2 = null,   // city
    string? AdminArea1 = null,   // state / province
    string? PostalCode = null);
