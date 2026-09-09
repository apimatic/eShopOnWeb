using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed binding of the <c>Maxio:</c> configuration section. Values are supplied via
/// configuration/user-secrets (never hard-coded) so the same build can target a different Maxio
/// site and catalog. See the section keys below.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (<c>Maxio:ApiKey</c>). Used as the HTTP Basic username, with "X" as password.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (<c>Maxio:Subdomain</c>), e.g. "cp-exp-8".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans (<c>Maxio:ProductFamilyHandle</c>).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL (<c>Maxio:BaseUrl</c>). When set it is used verbatim; otherwise
    /// the base URL is derived from <see cref="Subdomain"/> as <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address: the explicit <see cref="BaseUrl"/> override when
    /// provided, otherwise one derived from the subdomain.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        var baseUrl = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain}.chargify.com";

        // Ensure a trailing slash so relative request paths resolve correctly.
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        return new Uri(baseUrl, UriKind.Absolute);
    }

    /// <summary>Validates that the required settings are present, throwing a clear error if not.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it via user-secrets or environment configuration.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either Maxio:Subdomain or Maxio:BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or environment configuration.");
        }
    }
}
