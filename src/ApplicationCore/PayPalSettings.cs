namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. None of these values are
/// hard-coded anywhere in the repository — they are supplied through configuration (user-secrets / environment)
/// so the same build can run against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_SECTION = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Target PayPal environment, e.g. "sandbox".</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code used for every amount, e.g. "USD".</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional verbatim API base-URL override. When set, it is used as-is for every PayPal call — including the
    /// credential/token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
