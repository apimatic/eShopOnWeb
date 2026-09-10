using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal:</c> configuration section. Values are supplied from
/// secrets/environment (never committed); only the binding keys are fixed here. A missing or blank
/// required value stops the host from starting (fail-fast).
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target PayPal environment. Only <c>sandbox</c> is supported by the SDK in use.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency code used for every amount (e.g. USD).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every PayPal
    /// call, including the OAuth token request. When unset, the SDK's default sandbox host is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
