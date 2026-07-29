namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied at runtime
/// (user-secrets / environment) and are never committed to the repository. <see cref="BaseUrl"/> is an
/// optional override: when set it is used verbatim as the API base address; otherwise the address is
/// derived from <see cref="Subdomain"/>.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (bound from <c>Maxio:ApiKey</c>). Used as the Basic-auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (bound from <c>Maxio:Subdomain</c>).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product-family handle whose products are the subscribable plans (bound from <c>Maxio:ProductFamilyHandle</c>).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional explicit API base URL override (bound from <c>Maxio:BaseUrl</c>). When set, used as-is.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>True when the minimum settings needed to talk to Maxio are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));
}
