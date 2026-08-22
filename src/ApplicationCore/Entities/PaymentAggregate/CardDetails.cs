namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Card details supplied by a caller for a one-off PayPal request.
/// Must not be persisted or logged.
/// </summary>
public sealed class CardDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required string Name { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
}
