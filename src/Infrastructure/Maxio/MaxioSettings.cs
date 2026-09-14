using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Binds the <c>Maxio</c> configuration section. Values are supplied per environment
/// (user-secrets during development, environment variables or a vault elsewhere) and are
/// never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key. Sent as the HTTP Basic username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Maxio site, used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional. When set it is used verbatim as the API base address instead of deriving one
    /// from <see cref="Subdomain"/>. Needed for sites that are not on the default US host
    /// (for example EU sites, which live under a different domain).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Default host pattern for Maxio's US environment, per the Billing API server definitions.
    /// Override with <see cref="BaseUrl"/> for any other environment.
    /// </summary>
    private const string DefaultHostFormat = "https://{0}.chargify.com/";

    public bool IsConfigured => GetConfigurationErrors().Count == 0;

    /// <summary>
    /// Everything wrong with the current configuration, phrased for an operator reading logs.
    /// Deliberately never includes any configured value.
    /// </summary>
    public IReadOnlyList<string> GetConfigurationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"'{ConfigurationSectionName}:ApiKey' is missing.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"'{ConfigurationSectionName}:ProductFamilyHandle' is missing.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                errors.Add($"'{ConfigurationSectionName}:Subdomain' is missing (or set '{ConfigurationSectionName}:BaseUrl' to override the API base address).");
            }
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri) ||
                 (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add($"'{ConfigurationSectionName}:BaseUrl' is not an absolute http(s) URL.");
        }

        return errors;
    }

    /// <summary>
    /// The API base address: the <see cref="BaseUrl"/> override when supplied, otherwise the
    /// default host for the configured subdomain. Always ends in a slash so that relative
    /// request paths resolve underneath it rather than replacing it.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var errors = GetConfigurationErrors();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Maxio is not configured: {string.Join(" ", errors)}");
        }

        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, DefaultHostFormat, Subdomain!.Trim())
            : BaseUrl!.Trim();

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }
}
