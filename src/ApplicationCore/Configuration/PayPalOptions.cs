using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied by configuration
/// (user-secrets / environment) and never hard-coded, so the same build runs against any PayPal account.
/// The credential values live only in configuration, never in the repository.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientId is not configured.")]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientSecret is not configured.")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment, e.g. <c>sandbox</c>. The SDK ships only a Sandbox environment; a
    /// non-sandbox value requires <see cref="BaseUrl"/> to be set to that environment's API base.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Environment is not configured.")]
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency for all amounts, e.g. <c>USD</c>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Currency is not configured.")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>Optional base-URL override. When set, it is used verbatim as the API base for every
    /// PayPal call, including the OAuth token request.</summary>
    public string? BaseUrl { get; set; }

    public bool IsSandbox =>
        string.IsNullOrWhiteSpace(Environment) ||
        Environment.Equals("sandbox", System.StringComparison.OrdinalIgnoreCase);
}
