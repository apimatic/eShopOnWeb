namespace Microsoft.eShopWeb;

/// <summary>
/// Settings for connecting to Maxio Advanced Billing. Bound from the "Maxio" configuration
/// section. ApiKey must be supplied via user-secrets or environment variables, never committed.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Maxio API base address. When set, used verbatim instead of
    /// the URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetEffectiveBaseUrl()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl) ? BaseUrl! : $"https://{Subdomain}.chargify.com";
        return baseUrl.TrimEnd('/') + "/";
    }
}
