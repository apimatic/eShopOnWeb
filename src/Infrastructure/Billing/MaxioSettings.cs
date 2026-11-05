namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (use user-secrets / environment, never source control).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address instead of one derived from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional hosting region, "US" (default) or "EU". Only used when <see cref="BaseUrl"/> is not set.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Optional. How new subscriptions are collected: "remittance" (default, invoiced - no card needed), "automatic" or "prepaid".
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
