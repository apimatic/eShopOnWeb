namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off payment or to vault. Never persisted in this app's database and
/// never logged — passed straight to the PayPal gateway and discarded.
/// </summary>
public record CardDetails
{
    public required string Number { get; init; }
    /// <summary>Expiry in <c>YYYY-MM</c> form (PayPal wire format).</summary>
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public string? CardholderName { get; init; }

    // Optional billing address.
    public string? BillingLine1 { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    /// <summary>ISO-3166-1 alpha-2 country code (e.g. "US").</summary>
    public string? BillingCountryCode { get; init; }
}
