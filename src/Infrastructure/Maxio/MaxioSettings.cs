using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed configuration for the Maxio Advanced Billing integration, bound from the
/// <c>Maxio</c> configuration section. Values must never be hard-coded — the same build has to
/// run against a different Maxio site and catalog, so everything comes from configuration
/// (user-secrets / environment variables), keyed exactly as below.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio Chargify API key (used as the HTTP Basic username, with password <c>x</c>).
    /// Bound from <c>Maxio:ApiKey</c>.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Advanced Billing site subdomain. Bound from <c>Maxio:Subdomain</c>. Used to derive
    /// the API base address (<c>https://{Subdomain}.chargify.com</c>) unless <see cref="BaseUrl"/>
    /// is set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The handle of the product family whose products are offered as subscription plans.
    /// Bound from <c>Maxio:ProductFamilyHandle</c>.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. Bound from <c>Maxio:BaseUrl</c>. When set, it is used
    /// verbatim instead of deriving one from <see cref="Subdomain"/> — supporting non-default
    /// hosting (e.g. EU) or a proxy.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the effective API base address: <see cref="BaseUrl"/> when provided, otherwise
    /// the standard US host derived from <see cref="Subdomain"/>. Always returns a value with a
    /// trailing slash so relative request paths compose correctly.
    /// </summary>
    public Uri ResolveBaseAddress()
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

    /// <summary>Validates that the settings required to talk to Maxio are present.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Provide it via user-secrets or environment configuration.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain (or an explicit Maxio:BaseUrl) is not configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                "Maxio:ProductFamilyHandle is not configured.");
        }
    }
}
