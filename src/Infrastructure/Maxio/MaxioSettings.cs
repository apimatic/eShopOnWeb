using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the <c>Maxio:</c> section.
/// Values are supplied via environment / user-secrets and never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>The Maxio Chargify API key. Used as the HTTP Basic username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Advanced Billing site subdomain, used to derive the base URL when one is not set explicitly.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base URL is
    /// derived from <see cref="Subdomain"/> per the spec's server template (https://{site}.chargify.com).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address: the explicit <see cref="BaseUrl"/> if provided, else the
    /// spec's server template applied to the configured subdomain.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim();
            if (!trimmed.EndsWith('/')) trimmed += "/";
            return new Uri(trimmed, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    /// <summary>Throws when required configuration is missing, failing fast at startup.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio requires either Maxio:BaseUrl or Maxio:Subdomain to be configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }
    }
}
