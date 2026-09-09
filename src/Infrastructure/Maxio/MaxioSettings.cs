using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed settings for the Maxio Advanced Billing integration, bound from the <c>Maxio:</c>
/// configuration section. Secret values are supplied via user-secrets / environment configuration and
/// are never committed to the repository.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings are bound from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>The Maxio API key. Sent as the HTTP Basic auth username (password is the literal <c>x</c>).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. <c>cp-exp-6</c>. Used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL override. When set, it is used verbatim as the API base address;
    /// otherwise the base URL is derived as <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>True when the minimum settings required to call Maxio are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain))
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    /// <summary>Resolves the effective API base address from <see cref="BaseUrl"/> or <see cref="Subdomain"/>.</summary>
    public Uri ResolveBaseUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl!;

        // Ensure a trailing slash so relative request URIs resolve against the host, not a partial path.
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        return new Uri(baseUrl, UriKind.Absolute);
    }

    /// <summary>Validates that the required settings are present, throwing a descriptive error otherwise.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets (from the MAXIO_API_KEY environment variable).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is not configured. Set it via user-secrets (from the MAXIO_SITE_SUBDOMAIN environment variable), " +
                "or provide an explicit Maxio:BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets (from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable).");
        }
    }
}
