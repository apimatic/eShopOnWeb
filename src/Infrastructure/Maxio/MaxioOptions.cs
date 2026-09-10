using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the <c>Maxio</c> configuration
/// section. Values are supplied via .NET user-secrets / environment configuration and are
/// never stored in the repository.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSection = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic auth username (password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. <c>cp-exp-7</c>. Used to derive the base URL when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose plans are offered for subscription.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base
    /// address is derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base address (never has a trailing slash).</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }

    /// <summary>Fails fast with a clear message when required settings are missing.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"Missing '{ConfigSection}:ApiKey'. Set it in user-secrets from the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Missing '{ConfigSection}:ProductFamilyHandle'. Set it in user-secrets from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException(
                $"Missing '{ConfigSection}:Subdomain' (from MAXIO_SITE_SUBDOMAIN). Provide it, or set '{ConfigSection}:BaseUrl' explicitly.");
        }
    }
}
