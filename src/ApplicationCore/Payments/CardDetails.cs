namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string? CountryCode);

public sealed record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);
