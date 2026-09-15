namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Binds the <c>PayPal:</c> configuration section. Values are supplied via environment / user-secrets and
/// are never hard-coded, so the same build runs against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    /// <summary>From <c>PAYPAL_CLIENT_ID</c>.</summary>
    public string? ClientId { get; set; }

    /// <summary>From <c>PAYPAL_CLIENT_SECRET</c>.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>From <c>PAYPAL_ENVIRONMENT</c> (e.g. "sandbox").</summary>
    public string? Environment { get; set; }

    /// <summary>From <c>PAYPAL_CURRENCY</c> (ISO-4217, e.g. "USD").</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional. When set, used verbatim as the API base address for EVERY PayPal call (including the
    /// OAuth token request), instead of a base URL derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
