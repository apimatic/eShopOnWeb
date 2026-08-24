using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section.
/// Secrets (ApiKey) are supplied via user-secrets or environment variables, never appsettings.
/// </summary>
public class MaxioSettings
{
    public const string ConfigName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address;
    /// otherwise the base address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Payment collection method used when creating subscriptions. Defaults to
    /// "remittance" (invoice billing — no card required at signup). Use "automatic"
    /// when payment profiles are collected. Legacy Statements sites use "invoice".
    /// </summary>
    public string CollectionMethod { get; set; } = "remittance";

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets or the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured (and no Maxio:BaseUrl override was provided). " +
                "Set it via user-secrets or the MAXIO_SITE_SUBDOMAIN environment variable.");
        }
    }
}
