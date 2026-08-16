namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. This is a transient input only: it is
/// never persisted in this app's database and never written to logs. It flows straight through to
/// PayPal, which owns the card data.
/// </summary>
public sealed record CardDetails
{
    public required string Number { get; init; }
    public required int ExpiryMonth { get; init; }
    public required int ExpiryYear { get; init; }
    public required string SecurityCode { get; init; }
    public string? CardholderName { get; init; }
    public string? BillingLine1 { get; init; }
    public string? BillingLine2 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }

    /// <summary>Two-letter ISO country code for the billing address.</summary>
    public string? BillingCountryCode { get; init; }
    public string? BillingPostalCode { get; init; }
}
