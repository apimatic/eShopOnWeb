namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Values are supplied via .NET user-secrets / environment variables; none are hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (from MAXIO_API_KEY). Used as the Basic-auth username; password is "x" per the OpenAPI spec.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain (from MAXIO_SITE_SUBDOMAIN), used to build https://{subdomain}.chargify.com per the spec's server templating.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family whose products are offered as subscription plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional override for the API base address. When set, used verbatim instead of deriving from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl() =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.TrimEnd('/')
            : $"https://{Subdomain}.chargify.com";
}
