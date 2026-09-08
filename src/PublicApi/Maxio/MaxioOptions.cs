namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Values are supplied via
/// .NET user-secrets or environment variables; no values are stored in source.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    /// <summary>Sandbox/production API key used for HTTP Basic authentication.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain (e.g. "cp-exp-3").</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The product family handle that holds the subscription plans (e.g. "eshop-subscribe").</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional full API base URL. When set it is used verbatim instead of deriving
    /// a base URL from the subdomain.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the API base address. Honors <see cref="BaseUrl"/> verbatim when present,
    /// otherwise derives the US-hosted Advanced Billing URL from the subdomain.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
