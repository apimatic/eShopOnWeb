using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Binds the <c>PayPal:</c> configuration section. Values are supplied via configuration
/// (environment variables / user-secrets) and never hard-coded, so the same build runs against a
/// different PayPal account. Credentials are validated at startup (fail-fast) — see Program.cs.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_SECTION = "PayPal";

    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = null!;

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = null!;

    /// <summary>Logical environment (e.g. <c>sandbox</c>). Optional; the SDK targets PayPal sandbox by default.</summary>
    public string? Environment { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = null!;

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim as the base address for every PayPal
    /// call, including the OAuth token request, instead of the environment-derived default.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Whole-call timeout budget (seconds) enforced per PayPal call. Defaults to 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
