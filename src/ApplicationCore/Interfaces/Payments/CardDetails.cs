namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Raw card details supplied by a shopper for a one-off payment or to save a new card.
/// This type must never be logged or persisted - it exists only to cross the boundary
/// between the API request and the payment gateway call that vaults or authorizes it.
/// </summary>
public record CardDetails(
    string Number,
    string ExpiryYearMonth,
    string? SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null);
