using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration, bound from the <c>Maxio</c>
/// configuration section (§5). Only <see cref="ApiKey"/> is sensitive and belongs in user-secrets;
/// the rest is environment metadata.
/// </summary>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string ConfigurationSectionName = "Maxio";

    private const string UsHostTemplate = "https://{0}.chargify.com";
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Supplied through .NET user-secrets or environment variables — never committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain (e.g. <c>example-site</c>). Used to derive the host when no explicit base URL is set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region — <c>US</c> or <c>EU</c>. This is a different axis from the
    /// deployment target, which <see cref="BaseUrl"/> controls.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit outbound base URL. When set it wins verbatim over the subdomain-derived
    /// host, so the same build can be pointed at production, a dev/sandbox tenant, or a local mock
    /// server purely through configuration (§2.3).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The durable handle of the product family that holds the plans and metered component.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// The last-known product family id. Informational only: the client always resolves the live id
    /// from <see cref="ProductFamilyHandle"/> and warns when this value has drifted.
    /// </summary>
    public int? ProductFamilyId { get; set; }

    /// <summary>The handle of the plan offered by default (UC1's hero target).</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>The last-known default product id. Informational only; see <see cref="ProductFamilyId"/>.</summary>
    public int? DefaultProductId { get; set; }

    /// <summary>The handle of the alternate plan (UC3's upgrade/downgrade counterpart).</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>The last-known alternate product id. Informational only; see <see cref="ProductFamilyId"/>.</summary>
    public int? AlternateProductId { get; set; }

    /// <summary>The handle of the pay-as-you-go metered component (UC2).</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>The last-known metered component id. Informational only; see <see cref="ProductFamilyId"/>.</summary>
    public int? MeteredComponentId { get; set; }

    /// <summary>True when <see cref="Environment"/> selects Maxio's EU data centre.</summary>
    public bool IsEuRegion => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the outbound base URL: an explicit <see cref="BaseUrl"/> is used verbatim, otherwise
    /// the host is derived from <see cref="Subdomain"/> and the region. This is the single place the
    /// target server (production / dev tenant / local mock) is decided.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Neither an explicit base URL nor a subdomain is configured, or the explicit base URL is not a
    /// well-formed absolute HTTP(S) URL.
    /// </exception>
    /// <summary>
    /// Resolves the outbound base URL without throwing, for composition roots that must not fail to
    /// start when the <c>Maxio</c> section is absent. A misconfiguration then surfaces on the
    /// subscription paths as a typed billing error instead of taking the whole host down.
    /// </summary>
    public bool TryResolveBaseUrl(out string? baseUrl)
    {
        try
        {
            baseUrl = ResolveBaseUrl();

            return true;
        }
        catch (InvalidOperationException)
        {
            baseUrl = null;

            return false;
        }
    }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var explicitUrl = BaseUrl.Trim();

            if (!Uri.TryCreate(explicitUrl, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"'{ConfigurationSectionName}:{nameof(BaseUrl)}' must be an absolute http or https URL, but was '{explicitUrl}'.");
            }

            return explicitUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Configure either '{ConfigurationSectionName}:{nameof(BaseUrl)}' or '{ConfigurationSectionName}:{nameof(Subdomain)}' so the billing client knows which server to call.");
        }

        var template = IsEuRegion ? EuHostTemplate : UsHostTemplate;

        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }
}
