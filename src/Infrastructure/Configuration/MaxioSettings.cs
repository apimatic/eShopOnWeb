using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section in the same way <c>CatalogSettings</c> is bound.
/// </summary>
/// <remarks>
/// Only <see cref="ApiKey"/> is sensitive and it is supplied through .NET user-secrets or an
/// environment variable — never a file in the repository. The handles and identifiers are
/// environment metadata rather than secrets.
/// </remarks>
public class MaxioSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio data-center region for the US.</summary>
    private const string UnitedStatesRegion = "US";

    /// <summary>Maxio data-center region for the EU.</summary>
    private const string EuropeanUnionRegion = "EU";

    /// <summary>Host template for the US region.</summary>
    private const string UnitedStatesHostTemplate = "https://{0}.chargify.com";

    /// <summary>Host template for the EU region.</summary>
    private const string EuropeanUnionHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>
    /// The Maxio API key. Sent as the Basic-auth user name. Supplied through user-secrets or the
    /// <c>Maxio__ApiKey</c> environment variable.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. <c>apimatic-hackathon</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-center region — <c>US</c> or <c>EU</c>. This is the region the site lives
    /// in, which is a separate axis from the deployment target chosen by <see cref="BaseUrl"/>.
    /// </summary>
    public string Environment { get; set; } = UnitedStatesRegion;

    /// <summary>
    /// Optional explicit outbound base URL. When set it wins outright over the
    /// <see cref="Subdomain"/>-derived host, so the identical build can be pointed at production,
    /// a dev/sandbox tenant, or a local mock server purely through configuration.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that holds the plans and the metered component.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Numeric id of the product family. Maxio reassigns ids on a re-seed, so this is treated as
    /// a hint: the client prefers to resolve the family from <see cref="ProductFamilyHandle"/>.
    /// </summary>
    public int ProductFamilyId { get; set; }

    /// <summary>Handle of the plan the storefront offers by default.</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>Numeric id of the default plan. A hint only; the handle is authoritative.</summary>
    public int DefaultProductId { get; set; }

    /// <summary>Handle of the alternate plan, used as the upgrade/downgrade target.</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>Numeric id of the alternate plan. A hint only; the handle is authoritative.</summary>
    public int AlternateProductId { get; set; }

    /// <summary>Handle of the metered component pay-as-you-go usage is billed against.</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>Numeric id of the metered component. A hint only; the handle is authoritative.</summary>
    public int MeteredComponentId { get; set; }

    /// <summary>
    /// How many times a failed idempotent read is retried before the failure is surfaced. Kept
    /// deliberately small: these calls happen while a customer waits on a page render, so a long
    /// retry chain would turn a provider blip into an apparent hang. Writes are never retried.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Whether the configured region is the EU data center.</summary>
    public bool IsEuropeanRegion =>
        string.Equals(Environment, EuropeanUnionRegion, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the outbound base URL. An explicitly configured <see cref="BaseUrl"/> is used
    /// verbatim; only when it is absent is the host derived from <see cref="Subdomain"/> and the
    /// region. This is the single place retargeting happens, so pointing the integration at
    /// production, a dev tenant, or a local mock is a configuration change and never a code change.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when neither an explicit base URL nor a subdomain is configured, or when the
    /// explicit base URL is not a valid absolute URL.
    /// </exception>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var explicitBaseUrl = BaseUrl.Trim();

            // Require an http(s) scheme explicitly: "localhost:8080" parses as an absolute URI
            // with a "localhost" scheme, so a missing scheme would otherwise be accepted and then
            // fail confusingly on the first outbound call.
            if (!Uri.TryCreate(explicitBaseUrl, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"'{MaxioSettings.SectionName}:{nameof(BaseUrl)}' is set to '{explicitBaseUrl}', which is not a valid absolute http or https URL.");
            }

            return explicitBaseUrl;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Configure either '{MaxioSettings.SectionName}:{nameof(BaseUrl)}' or '{MaxioSettings.SectionName}:{nameof(Subdomain)}' so the billing client knows which server to target.");
        }

        var template = IsEuropeanRegion ? EuropeanUnionHostTemplate : UnitedStatesHostTemplate;

        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain.Trim());
    }
}
