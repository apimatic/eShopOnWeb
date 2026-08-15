namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio</c> configuration section. Values are supplied
/// through configuration/user-secrets/environment variables only — never hard-coded — so the
/// same build runs against a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Site API key (HTTP Basic username). From <c>Maxio:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain used to derive the API host. From <c>Maxio:Subdomain</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that holds the subscription plans. From <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base-URL override. When set, it is used verbatim as the API base
    /// address instead of deriving one from <see cref="Subdomain"/>. From <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
