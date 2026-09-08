namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/>, <see cref="Subdomain"/> and <see cref="ProductFamilyHandle"/>
/// are fed from the MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and
/// MAXIO_DEFAULT_PRODUCT_FAMILY environment variables respectively (see
/// <see cref="MaxioConfigurationExtensions"/>). None of their values are ever
/// hard-coded; the same build can target a different Maxio site/catalog by
/// changing the environment.
/// </remarks>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key used as the HTTP Basic username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. <c>cp-exp-6</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Hosting region per the Maxio OpenAPI <c>x-server-configuration</c>: US or EU.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>Handle of the product family that holds the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional full API base address override. When set it is used verbatim instead of
    /// one derived from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
