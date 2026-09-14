using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio</c> configuration
/// section. Values are supplied via user-secrets / environment configuration and must never be
/// hard-coded — the same build has to run against a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the username for HTTP Basic authentication (password is a literal "X").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain. The API base address is derived as <c>https://{Subdomain}.chargify.com</c> unless <see cref="BaseUrl"/> is set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are exposed as subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim instead of deriving one
    /// from <see cref="Subdomain"/>. Useful for pointing at a different Maxio host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> when provided, otherwise
    /// <c>https://{Subdomain}.chargify.com</c>.
    /// </summary>
    public Uri ResolveBaseUri()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain.Trim()}.chargify.com";

        // Ensure a trailing slash so relative request paths resolve correctly against the base.
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>True when all settings required to talk to Maxio are present.</summary>
    public bool IsConfigured => GetConfigurationErrors().Count == 0;

    /// <summary>Returns human-readable messages for each missing required setting (empty when fully configured).</summary>
    public IReadOnlyList<string> GetConfigurationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add("Maxio:ApiKey is not set (from the MAXIO_API_KEY environment variable).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            errors.Add("Maxio:Subdomain is not set (from MAXIO_SITE_SUBDOMAIN), and no Maxio:BaseUrl override was provided.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add("Maxio:ProductFamilyHandle is not set (from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable).");
        }

        return errors;
    }
}
