namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Full card details, used only in transit between the API boundary and the payment
/// processor. Never persisted, never logged.
/// </summary>
public sealed record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

public sealed record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);
