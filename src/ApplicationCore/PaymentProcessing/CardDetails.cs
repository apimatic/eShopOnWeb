namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// Raw card details supplied directly by the caller for a one-off payment or to save a card.
/// Never persisted by this application — forwarded to PayPal and discarded.
/// </summary>
public record CardDetails(
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
