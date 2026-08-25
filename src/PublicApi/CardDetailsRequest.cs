namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// One-off card details supplied directly in a request (pay-with-a-new-card, or save-a-card).
/// Never stored - the value objects built from this are handed to PayPal and then discarded.
/// </summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in "YYYY-MM" format.</summary>
    public string Expiry { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;

    public string? CardholderName { get; set; }

    /// <summary>
    /// Defaults to a placeholder US address when omitted, since PayPal's sandbox direct-card
    /// processor may decline a card payment/vault attempt that carries no billing address at all.
    /// </summary>
    public BillingAddressRequest BillingAddress { get; set; } = new();
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; } = "1 Microsoft Way";
    public string? AddressLine2 { get; set; }
    public string? City { get; set; } = "Redmond";
    public string? State { get; set; } = "WA";
    public string? PostalCode { get; set; } = "98052";
    public string CountryCode { get; set; } = "US";
}
