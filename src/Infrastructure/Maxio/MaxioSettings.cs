using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed settings for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio</c> configuration section. Values are supplied via .NET user-secrets / environment
/// and are never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>The Maxio API key, used as the HTTP Basic auth username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The handle of the product family whose products are exposed as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of deriving
    /// one from <see cref="Subdomain"/> — allowing the same build to target a different site.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address (always with a trailing slash so relative request paths
    /// combine correctly). Uses <see cref="BaseUrl"/> verbatim when provided; otherwise derives
    /// <c>https://{subdomain}.chargify.com/</c>.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(EnsureTrailingSlash(BaseUrl.Trim()), UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    /// <summary>Validates that the minimum required settings are present, failing fast at startup otherwise.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete: 'Maxio:ApiKey' is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete: 'Maxio:ProductFamilyHandle' is required.");
        }

        // Also validates that a base address can be resolved.
        _ = ResolveBaseUri();
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";
}
