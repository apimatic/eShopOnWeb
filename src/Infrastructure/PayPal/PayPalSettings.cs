using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied via environment
/// variables loaded into .NET user-secrets (never committed). The <see cref="Required"/> annotations,
/// combined with <c>ValidateDataAnnotations().ValidateOnStart()</c> at registration, make the host refuse
/// to boot when a credential is missing or blank — rather than discovering it as a 401 on the first call.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>PayPal REST client id (PayPal:ClientId ← PAYPAL_CLIENT_ID).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>PayPal REST client secret (PayPal:ClientSecret ← PAYPAL_CLIENT_SECRET).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target PayPal environment, e.g. "sandbox" (PayPal:Environment ← PAYPAL_ENVIRONMENT).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 settlement currency, e.g. "USD" (PayPal:Currency ← PAYPAL_CURRENCY).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every PayPal
    /// call including the OAuth token request, instead of the address derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Total per-call timeout budget in seconds (caller-visible deadline). Defaults to 30s.</summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;

    public bool IsSandbox =>
        string.IsNullOrWhiteSpace(Environment) ||
        Environment.Trim().Equals("sandbox", System.StringComparison.OrdinalIgnoreCase);
}
