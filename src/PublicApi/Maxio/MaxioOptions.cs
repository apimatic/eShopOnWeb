namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" section.
/// Values are supplied via user-secrets / environment; nothing is hard-coded so the same build
/// can target any Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key (Basic-auth user; password is conventionally "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain, e.g. "cp-exp-3" (used to derive the API base URL when
    /// <see cref="BaseUrl"/> is not set).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans to expose.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base URL override. When set it is used as-is instead of
    /// deriving "https://{Subdomain}.chargify.com".
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// True when enough configuration is present to call the Maxio API.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the API base address. <see cref="BaseUrl"/> wins verbatim when set;
    /// otherwise the base is derived from the subdomain (US Advanced Billing pattern).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return $"https://{Subdomain.Trim().ToLowerInvariant()}.chargify.com";
    }
}
