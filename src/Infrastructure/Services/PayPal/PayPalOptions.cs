using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal settings bound from the <c>PayPal:</c> configuration section. Values are supplied per
/// deployment (user-secrets / environment / secret store) and never hard-coded, so the same build runs
/// against a different PayPal account. Required fields are validated at startup (fail-fast).
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>From <c>PayPal:ClientId</c> (env <c>PAYPAL_CLIENT_ID</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>From <c>PayPal:ClientSecret</c> (env <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>From <c>PayPal:Environment</c> (env <c>PAYPAL_ENVIRONMENT</c>), e.g. "sandbox".</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>From <c>PayPal:Currency</c> (env <c>PAYPAL_CURRENCY</c>), ISO-4217, e.g. "USD".</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional <c>PayPal:BaseUrl</c> override. When set, it is used verbatim as the API base address for
    /// every PayPal call — including the OAuth token request — instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Whole-call timeout budget (seconds) enforced via a linked cancellation token.</summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 100;

    /// <summary>Hard safety cap on pages walked when building the reconciliation report.</summary>
    [Range(1, 10000)]
    public int MaxReconciliationPages { get; set; } = 200;
}
