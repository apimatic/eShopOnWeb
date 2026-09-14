namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the <c>Maxio:</c> configuration section.
///
/// Credentials arrive through environment variables and .NET user-secrets:
/// <list type="bullet">
/// <item><c>Maxio:ApiKey</c> &lt;- <c>MAXIO_API_KEY</c></item>
/// <item><c>Maxio:Subdomain</c> &lt;- <c>MAXIO_SITE_SUBDOMAIN</c></item>
/// <item><c>Maxio:ProductFamilyHandle</c> &lt;- <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c></item>
/// <item><c>Maxio:BaseUrl</c> - optional override of the API base address</item>
/// </list>
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }

    public string? Subdomain { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? BaseUrl { get; set; }

    /// <summary>
    /// Returns the API base address. When <see cref="BaseUrl"/> is configured it is used
    /// verbatim; otherwise it is derived from the site subdomain.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: no API base URL override was provided and 'Maxio:Subdomain' is empty. " +
                "Set the MAXIO_SITE_SUBDOMAIN environment variable (or Maxio user-secrets) and try again.");
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }

    /// <summary>
    /// Throws <see cref="MaxioConfigurationException"/> when a setting required to call the API is missing.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: 'Maxio:ApiKey' is empty. Set the MAXIO_API_KEY environment variable " +
                "(or the Maxio:ApiKey user-secret) and try again.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured: 'Maxio:ProductFamilyHandle' is empty. Set the " +
                "MAXIO_DEFAULT_PRODUCT_FAMILY environment variable (or the Maxio:ProductFamilyHandle user-secret) " +
                "and try again.");
        }

        ResolveBaseUrl();
    }
}
