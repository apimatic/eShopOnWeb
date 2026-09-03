using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio configuration, bound from the <c>Maxio:</c> configuration section. Secret
/// values are supplied out-of-repo (environment variables loaded into .NET user-secrets); only the
/// binding keys appear in code. Required members are validated at startup (fail-fast) so a missing
/// or blank credential stops the host from booting instead of surfacing as a 401 on the first call.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key — the Basic-auth username. From <c>MAXIO_API_KEY</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Site subdomain, substituted into <c>https://{site}.chargify.com</c>. From <c>MAXIO_SITE_SUBDOMAIN</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>API handle of the product family containing the subscription plans. From <c>MAXIO_DEFAULT_PRODUCT_FAMILY</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set (non-blank), it is used verbatim as the API base
    /// address instead of deriving one from <see cref="Subdomain"/> — e.g. to point at a mock server.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional handle of the plan used when a subscribe request omits one. Kept in configuration
    /// (not hard-coded) so the same build runs against a different catalog. When neither the request
    /// nor this setting supplies a plan, a subscribe request is rejected as invalid.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Total per-call deadline, in seconds, enforced by the service via a CancellationToken. The SDK
    /// retry <c>Timeout</c> is per-attempt, so this bounds the whole call. Default 100s.
    /// </summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// Payment collection method for new subscriptions. Because the plans require no payment method,
    /// subscriptions bill by invoice/remittance instead of auto-charging a card at signup. Valid wire
    /// values: <c>remittance</c> (Relationship Invoicing sites), <c>invoice</c> (legacy sites),
    /// <c>automatic</c>, <c>prepaid</c>. Default <c>remittance</c>.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
