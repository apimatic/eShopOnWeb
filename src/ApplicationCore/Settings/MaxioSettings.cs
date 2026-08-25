using System;

namespace Microsoft.eShopWeb.ApplicationCore.Settings;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Secrets (ApiKey) are supplied via environment variables / user-secrets, never from files in the repo.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim;
    /// otherwise the address is derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl!, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return new Uri($"https://{Subdomain}.chargify.com", UriKind.Absolute);
    }
}
