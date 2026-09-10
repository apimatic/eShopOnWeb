using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the "Maxio" configuration section.
/// Values are supplied via configuration/user-secrets and are never hard-coded, so the same build
/// can target a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Sent as the HTTP Basic username (password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the base address when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim; otherwise the base address
    /// is derived from <see cref="Subdomain"/> as https://{subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base address, validating that enough configuration is present.</summary>
    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var explicitUri))
            {
                throw new ArgumentException($"Maxio:BaseUrl '{BaseUrl}' is not a valid absolute URL.");
            }

            return explicitUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain (or Maxio:BaseUrl) must be configured.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com");
    }

    /// <summary>Returns true when the minimum configuration required to call Maxio is present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));
}
