namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Raw card details for a one-off (unsaved) payment or for vaulting. These are passed straight to
/// PayPal and are never persisted in the application's own database nor written to logs.
/// </summary>
public class CardDetails
{
    /// <summary>Primary account number, e.g. the sandbox Visa 4111111111111111.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form (PayPal's expected wire format).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? Name { get; set; }

    // Optional billing address (helps AVS; all optional for the sandbox card).
    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
}
