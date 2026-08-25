namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>Raw card details for a one-off payment or a new save-card request. Never persisted -
/// used only for the duration of the PayPal API call that consumes it.</summary>
public class PayPalCardDetails
{
    public required string Number { get; init; }
    /// <summary>"YYYY-MM".</summary>
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required string CardholderName { get; init; }
    public required string AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public required string City { get; init; }
    public string? State { get; init; }
    public required string PostalCode { get; init; }
    public required string CountryCode { get; init; }
}
