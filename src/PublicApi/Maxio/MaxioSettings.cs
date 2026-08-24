using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Secrets (ApiKey) are supplied via user-secrets or environment variables, never via appsettings files.
/// </summary>
public class MaxioSettings
{
    public const string ConfigName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!
            : $"https://{Subdomain}.chargify.com";
        return new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: 'Maxio:ApiKey' is missing. Provide it via .NET user-secrets or an environment variable.");
        }
        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: 'Maxio:ProductFamilyHandle' is missing.");
        }
    }
}
