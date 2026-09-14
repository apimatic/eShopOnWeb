namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section that drives the
/// Maxio Advanced Billing integration. Values are supplied via configuration/user-secrets
/// (never hard-coded) so the same build can target a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    /// <summary>Configuration section name these settings bind to.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key used as the HTTP Basic auth username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the API base address when no explicit override is set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base
    /// address is derived from <see cref="Subdomain"/> using the US production server template
    /// declared by the Maxio OpenAPI specification (<c>https://{site}.chargify.com</c>).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address: the <see cref="BaseUrl"/> override when provided,
    /// otherwise <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        return $"https://{Subdomain}.chargify.com/";
    }
}
