namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioOptions
{
    public const string SectionName = "Maxio";
    public const string HttpClientName = "MaxioAdvancedBilling";

    /// <summary>Bound from Maxio:ApiKey (MAXIO_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Bound from Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Bound from Maxio:ProductFamilyHandle (MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional. When set, used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Bound from Maxio:Environment (MAXIO_ENVIRONMENT). US or EU.</summary>
    public string Environment { get; set; } = "US";
}
