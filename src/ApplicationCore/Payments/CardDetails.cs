namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card data supplied by the caller for a single Finix tokenization request.
/// This object is never persisted or logged.
/// </summary>
public sealed record CardDetails(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string SecurityCode,
    string Name,
    CardAddress? BillingAddress);

public sealed record CardAddress(
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country);
