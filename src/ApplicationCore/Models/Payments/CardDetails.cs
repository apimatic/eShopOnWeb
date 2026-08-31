namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Full card details, used only in transit to the payment provider.
/// Never persisted and never logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode);
