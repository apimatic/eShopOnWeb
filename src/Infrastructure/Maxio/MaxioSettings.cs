namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Binds the <c>Maxio</c> configuration section. Values are supplied per deployment
/// (user-secrets / environment) and are never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key — the Basic-auth username. Bound from <c>Maxio:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, used to build the API base URL. Bound from <c>Maxio:Subdomain</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Product family handle whose products are the subscribable plans. Bound from <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim, overriding the URL derived
    /// from <see cref="Subdomain"/>. Bound from <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
