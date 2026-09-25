using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are
/// supplied by configuration (user-secrets in dev, environment variables in prod) and are never
/// hard-coded — the same build must run against a different PayPal account. Required parts are
/// validated at startup so a missing/blank credential refuses to boot rather than surfacing as a
/// 401 on the first call.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_SECTION = "PayPal";

    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The PayPal environment name, e.g. "sandbox".</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency code, e.g. "USD".</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim as the base address for every
    /// PayPal call — including the OAuth token request — instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
