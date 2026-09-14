using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed settings for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio:</c> configuration section. Values are supplied via .NET user-secrets / environment
/// and must never be committed to the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Site API key. Used as the HTTP Basic auth username (password is a dummy "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, used to derive the API base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base URL
    /// is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com/</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: the verbatim <see cref="BaseUrl"/> override when provided,
    /// otherwise <c>https://{subdomain}.chargify.com/</c>. Always returns a URI with a trailing
    /// slash so relative request paths compose correctly.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com/";

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Validates that the minimum settings required to reach Maxio are present.</summary>
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
                "Maxio:Subdomain is not configured. Set it via user-secrets (from the MAXIO_SITE_SUBDOMAIN environment variable), or provide Maxio:BaseUrl explicitly.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets (from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable).");
        }
    }
}
