using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Values are bound from the
/// "Maxio" configuration section (e.g. Maxio:ApiKey) and must not be hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public const string DefaultHostName = "chargify.com";

    public string? ApiKey { get; set; }

    public string? Subdomain { get; set; }

    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim instead of
    /// deriving "https://{subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address. Prefers <see cref="BaseUrl"/> when present; otherwise
    /// derives the standard Advanced Billing address from <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl!.TrimEnd('/') + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                $"Configuration value '{SectionName}:{nameof(Subdomain)}' is required when '{SectionName}:{nameof(BaseUrl)}' is not set.");
        }

        return new Uri($"https://{Subdomain.Trim()}.{DefaultHostName}/", UriKind.Absolute);
    }
}
