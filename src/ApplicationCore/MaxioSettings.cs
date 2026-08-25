using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the
/// "Maxio" configuration section. Values are supplied via user-secrets or
/// environment variables — never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (used as the Basic-auth username; password is "x" per the spec).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain (the {site} server variable in the spec).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family that holds the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: the explicit <see cref="BaseUrl"/> override when set,
    /// otherwise the spec's US production server template https://{site}.chargify.com.
    /// </summary>
    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }
}
