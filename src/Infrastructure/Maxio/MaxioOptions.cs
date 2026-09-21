namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio:</c> configuration
/// section. Values are supplied by configuration (user-secrets in development, environment variables in
/// production) and never hard-coded — the same build targets different Maxio sites/catalogs.
/// The required members are validated at startup by <see cref="MaxioOptionsValidator"/>, so the host
/// refuses to boot when any is missing or blank.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Required. Maxio (Chargify) API key. Sent as the HTTP Basic username. Bound from <c>Maxio:ApiKey</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Required. Maxio site subdomain, e.g. <c>your-site</c>. Bound from <c>Maxio:Subdomain</c>. Used to form the base URL unless <see cref="BaseUrl"/> is set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Required. Handle of the product family that contains the subscribable plans, e.g. <c>your-product-family</c>. Bound from <c>Maxio:ProductFamilyHandle</c>.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/>. Bound from <c>Maxio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional default plan handle used when a subscribe request omits one. Bound from <c>Maxio:DefaultPlanHandle</c>.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Payment collection method applied to new subscriptions. Defaults to <c>remittance</c> so subscriptions
    /// are invoiced rather than charged to a card on file — the plans in scope require no payment method, and
    /// the provider's own default (<c>automatic</c>) would attempt an immediate charge and fail without a card.
    /// Set to empty to accept the provider default, or to <c>invoice</c> for legacy Statements-architecture sites.
    /// Bound from <c>Maxio:PaymentCollectionMethod</c>.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Per-attempt HTTP timeout, in seconds (1–300). Bounds a single network attempt (not the whole call).</summary>
    public int PerAttemptTimeoutSeconds { get; set; } = 15;

    /// <summary>Total wall-clock budget for one billing operation (across all its provider calls), in seconds (1–600).</summary>
    public int TotalBudgetSeconds { get; set; } = 40;
}
