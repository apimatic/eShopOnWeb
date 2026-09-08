using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Hosting environment for the site. Supported values: US, EU.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional full API base URL. When set it is used verbatim instead of
    /// deriving an address from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(ApiKey)
            && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));
    }

    public string ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured. Set Maxio:Subdomain (or provide Maxio:BaseUrl).");
        }

        string host = Environment switch
        {
            "US" => $"{Subdomain}.chargify.com",
            "EU" => $"{Subdomain}.ebilling.maxio.com",
            _ => throw new InvalidOperationException(
                $"Maxio:Environment '{Environment}' is not supported. Supported values are 'US' and 'EU'.")
        };

        return $"https://{host}/";
    }
}
