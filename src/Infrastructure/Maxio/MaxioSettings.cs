using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the
/// "Maxio" configuration section. Values are supplied via .NET user-secrets /
/// environment configuration and are never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_SECTION = "Maxio";

    /// <summary>Maxio API key. Used as the Basic-auth username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (the "{subdomain}" in {subdomain}.chargify.com). Used to derive the API base address when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim; otherwise the
    /// base address is derived from <see cref="Subdomain"/> as https://{subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective API base address, honouring the <see cref="BaseUrl"/> override.</summary>
    public Uri ResolveBaseAddress()
    {
        var url = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl.Trim();

        // Ensure a trailing slash so relative request paths resolve predictably.
        if (!url.EndsWith('/'))
        {
            url += "/";
        }

        return new Uri(url, UriKind.Absolute);
    }

    /// <summary>Throws when required settings are missing, so misconfiguration fails fast at startup rather than on first request.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured. Set it via user-secrets or environment configuration.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain (or Maxio:BaseUrl) is not configured. Set it via user-secrets or environment configuration.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured. Set it via user-secrets or environment configuration.");
        }
    }
}
