namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Secrets arrive via
/// user-secrets / environment variables — never from a committed file.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional verbatim API base address; when set it wins over the subdomain-derived URL.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>"Us" (default) or "Eu".</summary>
    public string Environment { get; set; } = "Us";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));
}
