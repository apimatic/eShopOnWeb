namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>Billing address for a raw card payment. Never persisted.</summary>
public record PaymentAddress(string? AddressLine1, string? AdminArea2, string? AdminArea1, string? PostalCode, string CountryCode);

/// <summary>Raw card details for a one-off payment or a save-card request. Never persisted.</summary>
public record CardDetails(string Number, string ExpiryYearMonth, string SecurityCode, string? CardholderName, PaymentAddress? BillingAddress);

/// <summary>Exactly one of <see cref="Card"/> (one-off) or <see cref="VaultId"/> (a saved card) must be set.</summary>
public record PaymentSourceRequest(CardDetails? Card, string? VaultId);
