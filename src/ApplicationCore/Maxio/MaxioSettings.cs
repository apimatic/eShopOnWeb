using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section (values arrive via environment
/// variables and/or user-secrets; never commit credentials).
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>
    /// Maxio/Advanced Billing API key, used as the Basic-auth username
    /// (the password component is the literal "X").
    /// Configured via "Maxio:ApiKey".
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Site subdomain used to derive the API base URL as
    /// https://{Subdomain}.chargify.com when <see cref="BaseUrl"/> is not set.
    /// Configured via "Maxio:Subdomain".
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the purchasable plans.
    /// Configured via "Maxio:ProductFamilyHandle".
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base
    /// address instead of deriving one from <see cref="Subdomain"/>.
    /// Configured via "Maxio:BaseUrl".
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the REST API base address: the explicit override when present,
    /// otherwise derived from the subdomain (US environment hosting).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return EnsureTrailingSlash(BaseUrl!.Trim());
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio settings are incomplete: set either '{CONFIG_NAME}:BaseUrl' or '{CONFIG_NAME}:Subdomain'.");
        }

        return EnsureTrailingSlash($"https://{Subdomain.Trim()}.chargify.com");
    }

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith("/") ? url : url + "/";
}
