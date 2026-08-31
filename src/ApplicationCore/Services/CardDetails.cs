namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Card details supplied for a one-off payment or for vaulting. Carried in memory only —
/// never persisted to the application's database and never written to logs.
/// </summary>
public class CardDetails
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }

    public string? BillingAddressLine1 { get; set; }
    public string? BillingAddressLine2 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
}
