namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Secrets (ApiKey) are supplied via user-secrets or environment variables, never via appsettings files.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio Billing API key (used as the Basic-auth username; password is literally "X").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, e.g. "mysite" for https://mysite.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of
    /// deriving https://{Subdomain}.chargify.com.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string EffectiveBaseUrl =>
        !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl.TrimEnd('/')
            : string.IsNullOrWhiteSpace(Subdomain)
                ? string.Empty
                : $"https://{Subdomain}.chargify.com";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(EffectiveBaseUrl);
}
