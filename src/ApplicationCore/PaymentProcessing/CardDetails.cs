namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// Raw one-off card details for a single payment or a vaulting request. Never persisted -
/// used only to build a single outbound PayPal request and then discarded.
/// </summary>
public class CardDetails
{
    public required string Number { get; init; }
    /// <summary>Expiry in "YYYY-MM" format.</summary>
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public string? CardholderName { get; init; }
    /// <summary>
    /// Optional, but PayPal's sandbox direct-card processor may decline (422
    /// TRANSACTION_REFUSED) an authorization/vault attempt that carries none at all - supply it
    /// when available.
    /// </summary>
    public BillingAddress? BillingAddress { get; init; }
}

public class BillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public required string CountryCode { get; init; }
}
