using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio</c> configuration
/// section. Values are supplied by .NET user-secrets / environment and are never committed to the
/// repository. The <see cref="Required"/> attributes drive fail-fast validation at startup
/// (see <see cref="MaxioServiceCollectionExtensions"/>) — a missing OR blank credential stops the
/// host from booting rather than surfacing later as a 401 on the first call.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Maxio Chargify API key (bound from <c>Maxio:ApiKey</c>, sourced from MAXIO_API_KEY).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (bound from <c>Maxio:Subdomain</c>, sourced from MAXIO_SITE_SUBDOMAIN).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Product family handle whose plans are offered (bound from <c>Maxio:ProductFamilyHandle</c>, sourced from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override (bound from <c>Maxio:BaseUrl</c>). When set, it is used verbatim
    /// as the API base address instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
