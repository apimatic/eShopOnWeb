using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio:</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> is a secret: supply it through user-secrets, environment variables or a vault —
/// never through a checked-in <c>appsettings*.json</c>.
/// </remarks>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Sent as the basic-auth username.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ApiKey is required.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain, e.g. <c>cp-exp-2</c>. Substituted into the provider's base-URL template
    /// unless <see cref="BaseUrl"/> overrides it with a literal address.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:Subdomain is required.")]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family whose products are offered as subscription plans, e.g.
    /// <c>eshop-subscribe</c>. Handles are stable; the family's numeric id is not, so only the handle is
    /// configured.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ProductFamilyHandle is required.")]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base-address override. When set it is used verbatim, instead of deriving the address
    /// from <see cref="Subdomain"/>. Useful for a mock server, a proxy, or a non-default Maxio host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Handle of the plan used when a subscribe request does not name one. When empty, the first plan the
    /// product family returns is used.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How Maxio should collect a new subscription's balance — one of <c>remittance</c>, <c>invoice</c>,
    /// <c>automatic</c> or <c>prepaid</c>.
    /// </summary>
    /// <remarks>
    /// Leave unset to derive it from the site: <c>remittance</c> on a Relationship Invoicing site,
    /// <c>invoice</c> on the legacy Statements architecture. Which of the two is valid depends on the
    /// architecture, so the value is never hard-coded. It must not be <c>automatic</c> here: that asks
    /// Maxio to charge a card immediately, and this API captures none, so enrollment would be rejected.
    /// </remarks>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Total budget for one provider call, including retries and backoff.</summary>
    public TimeSpan CallBudget { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Bound on a single HTTP attempt. Several attempts can occur inside <see cref="CallBudget"/>.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Retries the SDK may make for a transient failure, over and above the first attempt.</summary>
    [Range(1, 10)]
    public int MaxRetries { get; set; } = 2;

    /// <summary>How long the resolved product-family id is cached before it is looked up again.</summary>
    public TimeSpan CatalogCacheDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Logs every outgoing Maxio request and its status at <c>Debug</c> level. Useful when verifying a new
    /// call on the wire; leave off otherwise.
    /// </summary>
    public bool LogRequests { get; set; }
}
