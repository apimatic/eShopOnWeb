using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing configuration, bound from the <c>Maxio:</c> configuration section. Values are
/// supplied at deployment time (here: from environment variables loaded into .NET user-secrets) and are
/// never hard-coded — the same build must run against a different Maxio site and catalog.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key. Bound from <c>Maxio:ApiKey</c> (env <c>MAXIO_API_KEY</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain. Bound from <c>Maxio:Subdomain</c> (env <c>MAXIO_SITE_SUBDOMAIN</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family whose products are the subscribable plans. Bound from
    /// <c>Maxio:ProductFamilyHandle</c> (env <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL override, bound from <c>Maxio:BaseUrl</c>. When set it is used
    /// verbatim as the API base address; when unset the base URL is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
