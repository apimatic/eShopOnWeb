namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Raw card data supplied by the shopper for a one-off payment or to be vaulted. This type is only ever
/// held in memory for the duration of a single request and is never persisted or logged.
/// </summary>
public class CardDetails
{
    /// <summary>Primary account number, e.g. 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form, e.g. 2030-01.</summary>
    public string Expiry { get; set; } = string.Empty;

    /// <summary>Card security / CVC code.</summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>Optional cardholder name.</summary>
    public string? CardholderName { get; set; }

    /// <summary>Optional billing address.</summary>
    public BillingAddressDetails? BillingAddress { get; set; }
}
