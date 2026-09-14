using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Connection settings for the Maxio Advanced Billing (Billing API) site.
/// Values are bound from the "Maxio" configuration section; secrets must come
/// from user-secrets or environment variables, never from source control.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of
    /// deriving one from the Subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set Maxio:Subdomain (or Maxio:BaseUrl) and Maxio:ApiKey.");
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
