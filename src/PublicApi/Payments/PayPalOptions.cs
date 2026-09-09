using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. No value is ever hard-coded — the
/// same build runs against a different PayPal account purely by changing configuration.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment; only <c>sandbox</c> is exposed by the SDK.</summary>
    [Required]
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency code (e.g. USD). Amounts are held/captured in this currency.</summary>
    [Required]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim for every PayPal call — including the
    /// OAuth token request — instead of the environment default.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Whole-call timeout budget for a single PayPal operation.</summary>
    public int RequestTimeoutSeconds { get; set; } = 100;
}
