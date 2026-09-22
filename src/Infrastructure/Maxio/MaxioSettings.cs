namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Options bound from the <c>Maxio:</c> configuration section. Values are supplied per deployment
/// (user-secrets / environment) and are never hard-coded — the same build targets different Maxio
/// sites and catalogs.
/// </summary>
public class MaxioSettings
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key. Bound from <c>Maxio:ApiKey</c> (env <c>MAXIO_API_KEY</c>). Secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, used to build <c>https://{subdomain}.chargify.com</c>. Bound from <c>Maxio:Subdomain</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Product family handle whose products are the subscription plans. Bound from <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/>. Bound from <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
