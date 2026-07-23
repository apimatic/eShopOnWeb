using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section (plan.md §2.3, §5). Only <see cref="ApiKey"/> is sensitive and it must arrive through
/// .NET user-secrets or an environment variable — never from a file in the repository.
/// </summary>
public class MaxioSettings : ISubscriptionSettings
{
    /// <summary>The configuration section these settings bind from.</summary>
    public const string SectionName = "Maxio";

    private const string UsHostTemplate = "https://{0}.chargify.com";
    private const string EuHostTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>The Maxio API key. Supplied through user-secrets / environment, never committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. <c>cp-exp-2</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio data-centre region — <c>US</c> or <c>EU</c>. This is a different axis from the deployment
    /// target, which <see cref="BaseUrl"/> controls (plan.md §2.3).
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Explicit outbound base URL. When set it wins over the <see cref="Subdomain"/>-derived host, so the
    /// identical build can be pointed at production, a dev/sandbox tenant, or a local mock purely through
    /// configuration (plan.md §2.3). Leave empty to use the derived host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that holds the plans and the metered component.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional pre-resolved product family id. Handles are durable but Maxio's numeric ids are reassigned
    /// on a re-seed, so when this is absent the id is resolved from <see cref="ProductFamilyHandle"/> at
    /// runtime (plan.md §1.3).
    /// </summary>
    public int? ProductFamilyId { get; set; }

    /// <inheritdoc />
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <inheritdoc />
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <inheritdoc />
    public string MeteredComponentHandle { get; set; } = string.Empty;

    /// <summary>
    /// How Maxio should collect payment for new subscriptions: <c>Remittance</c> (invoice the customer) or
    /// <c>Automatic</c> (charge a stored payment method).
    /// </summary>
    /// <remarks>
    /// The default is <c>Remittance</c>, because the demo enrolls customers without capturing a card and a
    /// site whose default is automatic collection rejects such an enrollment outright ("No payment method
    /// was on file"). A deployment that does capture cards sets this to <c>Automatic</c> in configuration —
    /// no code change (plan.md UC0, UC1).
    /// </remarks>
    public string PaymentCollectionMethod { get; set; } = "Remittance";

    /// <summary>How long resolved catalog identifiers stay cached before they are looked up again.</summary>
    public TimeSpan CatalogCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>True when an explicit <see cref="BaseUrl"/> override has been configured.</summary>
    public bool HasExplicitBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);

    /// <summary>True when the configured <see cref="Environment"/> selects Maxio's EU region.</summary>
    public bool IsEuRegion => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The outbound base URL the billing client targets: the explicit <see cref="BaseUrl"/> verbatim when
    /// one is configured, otherwise the host derived from <see cref="Subdomain"/> and the region. This is
    /// the single place retargeting happens (plan.md §2.3, §4.3).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (HasExplicitBaseUrl)
        {
            return BaseUrl!.Trim();
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio is not configured: set either '{SectionName}:BaseUrl' or '{SectionName}:Subdomain'.");
        }

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            IsEuRegion ? EuHostTemplate : UsHostTemplate,
            Subdomain.Trim());
    }

    /// <summary>
    /// Validates that everything the integration needs is present. Called from the composition root so a
    /// misconfiguration is reported once, clearly, rather than as an obscure failure on the first call.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"'{SectionName}:ApiKey' is not configured. Set it with: dotnet user-secrets set \"{SectionName}:ApiKey\" \"<key>\"");
        }

        if (!HasExplicitBaseUrl && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Configure either '{SectionName}:BaseUrl' or '{SectionName}:Subdomain'.");
        }

        if (HasExplicitBaseUrl && !Uri.TryCreate(BaseUrl!.Trim(), UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"'{SectionName}:BaseUrl' is not an absolute URL: '{BaseUrl}'.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException($"'{SectionName}:ProductFamilyHandle' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(DefaultProductHandle))
        {
            throw new InvalidOperationException($"'{SectionName}:DefaultProductHandle' is not configured.");
        }

        if (string.IsNullOrWhiteSpace(MeteredComponentHandle))
        {
            throw new InvalidOperationException($"'{SectionName}:MeteredComponentHandle' is not configured.");
        }
    }
}
