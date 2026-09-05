namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values come from user-secrets / environment
/// in every environment - none are hard-coded, so the same build can target a different Maxio site
/// (and a different product catalog) without a code change.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key for the target site (used as the Basic Auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "cp-exp-4" for https://cp-exp-4.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans, e.g. "eshop-subscribe".</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of deriving one
    /// from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// "US" (default) or "EU" - selects which Maxio-hosted domain to derive the base address from
    /// when <see cref="BaseUrl"/> is not set. Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Environment { get; set; } = "US";

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var host = string.Equals(Environment, "EU", System.StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain}.ebilling.maxio.com"
            : $"{Subdomain}.chargify.com";

        return $"https://{host}";
    }
}
