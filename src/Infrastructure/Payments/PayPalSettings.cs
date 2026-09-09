namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal:</c> section. Values are supplied via
/// .NET user-secrets / environment and are never hard-coded in the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Either <c>sandbox</c> or <c>live</c>/<c>production</c>. Used to derive the API base URL.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>The three-letter settlement currency (e.g. USD).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit base URL override. When set, it is used verbatim as the API base address for
    /// every PayPal call (including the OAuth token request), instead of one derived from Environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base address: the override if present, otherwise per-environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var env = Environment?.Trim().ToLowerInvariant();
        return env is "live" or "production"
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
