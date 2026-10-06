namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the
/// "Maxio" configuration section. Values come from user-secrets / environment
/// variables — never hard-coded: <c>Maxio:ApiKey</c> (from <c>MAXIO_API_KEY</c>),
/// <c>Maxio:Subdomain</c> (from <c>MAXIO_SITE_SUBDOMAIN</c>) and
/// <c>Maxio:ProductFamilyHandle</c> (from <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>).
/// <para>
/// <c>Maxio:BaseUrl</c> is an optional override: when set it is used verbatim as
/// the API base address; otherwise the address is derived from the subdomain.
/// </para>
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }

    public string? Subdomain { get; set; }

    public string? BaseUrl { get; set; }

    public string? ProductFamilyHandle { get; set; }
}