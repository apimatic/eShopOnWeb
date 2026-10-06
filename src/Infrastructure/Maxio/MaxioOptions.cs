using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Values are supplied via user-secrets / environment, never committed to the repo.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key. Used as the Basic-auth username with "X" as password.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain, used to derive the API base URL
    /// ("https://{subdomain}.chargify.com") when <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscribable plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address override (e.g. an EU site or a custom domain).
    /// When set it is used instead of the subdomain-derived URL.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when set,
    /// otherwise derived from <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/'));
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio billing is not configured: set {CONFIG_NAME}:BaseUrl or {CONFIG_NAME}:Subdomain.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com");
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ProductFamilyHandle);
}