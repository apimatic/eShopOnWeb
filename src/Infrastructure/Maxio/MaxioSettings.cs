namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio:</c>
/// configuration section. Values are supplied via environment/user-secrets and are never
/// hard-coded, so the same build runs against any Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    /// <summary>Section name in configuration.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (used as the HTTP Basic auth username, with password "X").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, used to derive the API base address when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of deriving one
    /// from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// True when enough configuration is present to talk to Maxio: an API key, a product family
    /// handle, and either a subdomain or an explicit base URL.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>
    /// Resolves the API base address (always with a trailing slash). Uses <see cref="BaseUrl"/>
    /// verbatim when provided, otherwise derives <c>https://{subdomain}.chargify.com/</c>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/') + "/";
        }

        return $"https://{Subdomain}.chargify.com/";
    }
}
