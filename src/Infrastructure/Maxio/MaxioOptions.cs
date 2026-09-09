namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing API. Bound from the "Maxio" configuration
/// section; values are supplied via user-secrets or environment variables, never committed.
/// </summary>
/// <remarks>
/// Supported keys: <c>Maxio:ApiKey</c>, <c>Maxio:Subdomain</c>, <c>Maxio:Environment</c>
/// ("US" or "EU", defaults to "US" per the OpenAPI spec's default server configuration),
/// <c>Maxio:ProductFamilyHandle</c> and the optional <c>Maxio:BaseUrl</c> override, which
/// is used verbatim as the API base address when set.
/// </remarks>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? Environment { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Spec Collection-Method used when creating subscriptions. Defaults to "remittance"
    /// (Relationship Invoicing: the customer is billed by invoice and pays off-session),
    /// which lets shoppers subscribe without card capture. Configure "automatic" for a
    /// site with a payment gateway.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
