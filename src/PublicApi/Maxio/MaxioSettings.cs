namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio</c> configuration
/// section. Values are supplied at runtime (user-secrets / environment) and are never stored in the
/// repository. See <see cref="SectionName"/>.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (used as the HTTP Basic username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. <c>my-site</c>; used to derive the API base URL.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim instead of deriving the base URL
    /// from <see cref="Subdomain"/> (useful for a proxy, a mock server, or a non-default region).
    /// </summary>
    public string? BaseUrl { get; set; }
}
