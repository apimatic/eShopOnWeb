namespace Microsoft.eShopWeb;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section.
/// Values come from environment variables / user-secrets — never from source control.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base address override. When set, used verbatim instead of
    /// deriving <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Subdomain)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    /// <summary>
    /// Resolves the Billing API base address. <see cref="BaseUrl"/> wins when present;
    /// otherwise the site subdomain is expanded to the Chargify host documented by Maxio.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return string.Empty;
        }

        return $"https://{Subdomain.Trim()}.chargify.com/";
    }
}
