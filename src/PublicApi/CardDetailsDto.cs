namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Raw card details as supplied by an API caller. Never persisted or logged by this application.
/// </summary>
public record CardDetailsDto(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);
