namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// Raw card details submitted for a one-off payment or to save a card. Never persisted by this
/// application — only handed to the payment gateway for the single call that needs it.
/// </summary>
public record CardDetails(
    string Name,
    string Number,
    string Expiry, // "YYYY-MM"
    string SecurityCode,
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null);
