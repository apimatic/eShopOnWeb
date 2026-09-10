using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed configuration for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio</c> configuration section. Values are supplied via .NET user-secrets / environment
/// configuration and are never committed to the repository.
/// </summary>
public sealed class MaxioSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Maxio";

    /// <summary>The Maxio API key used for HTTP Basic authentication (as the username, password "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain (e.g. "acme" for acme.chargify.com).</summary>
    public string? Subdomain { get; set; }

    /// <summary>The handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL is
    /// derived from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address, honoring <see cref="BaseUrl"/> when present and
    /// otherwise deriving the US Advanced Billing host from <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim();
            // Ensure a trailing slash so relative request paths resolve correctly.
            if (!trimmed.EndsWith('/'))
            {
                trimmed += "/";
            }

            return new Uri(trimmed, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    /// <summary>Validates that the minimum configuration needed to call Maxio is present.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio configuration is missing 'Maxio:ApiKey'.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio configuration is missing 'Maxio:ProductFamilyHandle'.");
        }

        // Forces base-URL resolution to validate that Subdomain or BaseUrl was supplied.
        _ = ResolveBaseUri();
    }
}
