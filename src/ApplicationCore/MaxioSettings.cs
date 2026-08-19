using System;

namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values must come from
/// environment / user-secrets — never from committed files.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    /// <summary>
    /// When <see cref="BaseUrl"/> is set, it is used verbatim as the API base
    /// address. Otherwise the address is derived from <see cref="Subdomain"/>
    /// against the US Advanced Billing host (*.chargify.com).
    /// </summary>
    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));
}
