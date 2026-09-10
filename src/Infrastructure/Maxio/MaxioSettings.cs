using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied
/// via configuration/user-secrets (never hard-coded), so the same build runs against any
/// Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Per-site API key (Basic-auth username; the password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. "cp-exp-8". Used to derive the API base URL.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are the subscribable plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base
    /// URL is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>.
    /// (The Advanced Billing API is still served from the chargify.com domain post-rebrand.)
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address as an absolute URI ending in a trailing slash so that
    /// relative request paths compose correctly against it.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain}.chargify.com";

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>
    /// Validates that the minimum settings required to talk to Maxio are present, throwing a
    /// clear startup error otherwise.
    /// </summary>
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
                "Maxio configuration requires either Maxio:BaseUrl or Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN).");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
    }
}
