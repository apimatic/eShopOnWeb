using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the <c>Maxio</c> configuration section.
/// Values are supplied by configuration (user-secrets in development) and are never hard-coded — the same
/// build must run against a different Maxio site and catalog. The <see cref="Required"/> attributes drive
/// startup fail-fast validation: <see cref="RequiredAttribute"/> treats a blank/whitespace string as
/// invalid, so a blank credential is rejected as firmly as a missing one.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio (Chargify) API key — used as the HTTP Basic-auth username (password is the literal "x").</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain — substituted into the Production/US base URL <c>https://{site}.chargify.com</c>.</summary>
    [Required]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The product family whose products are offered as subscription plans (handle, not numeric id).</summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> (useful for a mock server or a non-default host).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional default plan handle used by <c>POST /api/subscriptions</c> when the caller supplies none.
    /// When unset, the first available plan in the family is used.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>Total per-operation timeout budget, in seconds, enforced via a CancellationToken deadline.</summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;
}
