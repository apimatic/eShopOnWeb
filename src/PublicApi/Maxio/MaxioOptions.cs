namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio" configuration
/// section. Secret values (API key) are supplied via user-secrets / environment variables and
/// must never be committed to the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of deriving
    /// https://{Subdomain}.chargify.com from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional. Plan (product) handle used when a subscribe request does not name a plan.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How Maxio collects payment for created subscriptions. "remittance" (invoice-based)
    /// allows subscribing without capturing a payment method.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
