namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal settings bound from the <c>PayPal</c> configuration section. Values are supplied via
/// configuration / user-secrets (never hard-coded), e.g. <c>PayPal:ClientId</c>,
/// <c>PayPal:ClientSecret</c>, <c>PayPal:Environment</c>, <c>PayPal:BaseUrl</c>.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Target PayPal environment. Only <c>sandbox</c> is supported by this integration.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>
    /// Optional verbatim API base-URL override. When set, it is used exactly as given instead of the
    /// environment's default host — useful for pointing at a mock or a self-hosted gateway.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Overall per-call budget (seconds) applied on top of the SDK's per-attempt timeout.</summary>
    public int CallTimeoutSeconds { get; set; } = 30;
}
