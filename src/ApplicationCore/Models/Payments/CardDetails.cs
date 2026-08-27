namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Full card details as supplied by the shopper for a one-off payment or for vaulting.
/// These are transient: they are forwarded to PayPal and are never persisted or logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);
