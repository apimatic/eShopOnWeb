using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Strongly-typed PayPal settings, bound from the <c>PayPal:</c> configuration section. Values are
/// never hard-coded — credentials come from user-secrets / environment, so the same build runs
/// against a different PayPal account. Missing/blank required values fail the app at startup.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment (e.g. <c>sandbox</c>). Bound from <c>PayPal:Environment</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency code. Bound from <c>PayPal:Currency</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request. When null, the address is derived from the
    /// selected environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Whole-call timeout budget (seconds) enforced by the processor via a CancellationToken.</summary>
    public int TotalTimeoutSeconds { get; set; } = 30;

    /// <summary>Per-attempt HTTP timeout (seconds) for the SDK retry pipeline.</summary>
    public int PerAttemptTimeoutSeconds { get; set; } = 15;
}
