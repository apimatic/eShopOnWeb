using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the
/// "Maxio" configuration section. Secret values come from user-secrets /
/// environment variables and are never stored in the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key, used as the basic-auth username (password is "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The subdomain of the Advanced Billing site (e.g. "acme" for
    /// https://acme.chargify.com). Used to derive the base URL when
    /// <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Hosting environment of the site per the OpenAPI server configuration:
    /// "US" (https://{site}.chargify.com) or "EU"
    /// (https://{site}.ebilling.maxio.com). Only used to derive the base URL.
    /// Defaults to "US".
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base
    /// address instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Handle of the product family that contains the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Resolves the API base address: BaseUrl verbatim when configured,
    /// otherwise derived from the hosting environment + subdomain as defined
    /// by the x-server-configuration section of the Maxio OpenAPI spec.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var subdomain = Subdomain?.Trim();
        if (string.IsNullOrEmpty(subdomain))
        {
            return string.Empty;
        }

        var host = string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{subdomain}.ebilling.maxio.com"
            : $"{subdomain}.chargify.com";
        return $"https://{host}";
    }
}
