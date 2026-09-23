using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the <c>PayPal</c> configuration section. Values are supplied at runtime
/// (environment variables → user-secrets) and never committed to the repository.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    /// <summary>From <c>PAYPAL_CLIENT_ID</c>. The sandbox/live REST client id.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_CLIENT_SECRET</c>. The sandbox/live REST client secret.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_ENVIRONMENT</c>. Only <c>sandbox</c> maps to a native SDK environment;
    /// any other value requires <see cref="BaseUrl"/> to be set to that environment's base address.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_CURRENCY</c>. The ISO-4217 currency for every amount.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>Optional override. When set, it is used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request. When blank, the SDK's environment default is used.</summary>
    public string? BaseUrl { get; set; }

    public bool IsSandbox =>
        string.Equals(Environment?.Trim(), "sandbox", System.StringComparison.OrdinalIgnoreCase);
}
