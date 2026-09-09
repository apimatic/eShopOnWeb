using System;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the "Maxio" configuration section.
/// Values are supplied via configuration/user-secrets (never hard-coded), so the same build can
/// target different Maxio sites and catalogs.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>API key used as the HTTP Basic auth username (password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain; the API base is derived as https://{Subdomain}.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim instead of deriving one from
    /// <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// True when enough settings are present to talk to Maxio: an API key, a product family handle,
    /// and either an explicit base URL or a subdomain to derive one from.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain));

    /// <summary>
    /// Resolves the API base address: the explicit <see cref="BaseUrl"/> when provided, otherwise
    /// https://{Subdomain}.chargify.com. A trailing slash is ensured so relative request paths
    /// combine correctly.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain}.chargify.com";

        if (!raw.EndsWith("/", StringComparison.Ordinal))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Builds the HTTP Basic authorization header value (base64 of "{ApiKey}:x").</summary>
    public string BuildBasicAuthParameter()
        => Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ApiKey}:x"));

    /// <summary>Throws <see cref="Subscriptions.BillingNotConfiguredException"/> when misconfigured.</summary>
    public void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new ApplicationCore.Subscriptions.BillingNotConfiguredException(
                "Maxio billing is not configured. Provide Maxio:ApiKey, Maxio:ProductFamilyHandle and " +
                "either Maxio:Subdomain or Maxio:BaseUrl (via user-secrets or environment configuration).");
        }
    }
}
