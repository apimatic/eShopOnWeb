namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// Transient card details forwarded to PayPal. Never persist or log this type.
/// </summary>
public sealed record CardPaymentSource(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public sealed record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode);
