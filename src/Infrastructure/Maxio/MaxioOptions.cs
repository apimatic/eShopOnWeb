namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. No values are ever hard-coded;
/// credentials are expected to arrive via user-secrets or environment variables
/// (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY).
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key, used as the HTTP Basic username ("X" is the password).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The site subdomain used to derive the API base address
    /// (https://{subdomain}.chargify.com/). Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family that contains the subscription plans
    /// offered by eShopOnWeb.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address override (e.g. for EU environments or
    /// the API gateway). When set, it is used verbatim instead of deriving the
    /// address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional handle of the plan used when a subscribe request does not
    /// specify one. When neither is supplied, the request is rejected.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Resolves the effective API base address, always ending with a trailing slash.
    /// </summary>
    public string ResolveBaseUrl()
    {
        var baseAddress = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl.TrimEnd('/') + "/";

        return baseAddress;
    }
}