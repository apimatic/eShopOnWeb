namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the <c>PayPal:</c> configuration section. None of these values are hard-coded — the same
/// build runs against any PayPal account by supplying different configuration.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Selects the default API base address.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for every amount (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for every PayPal call —
    /// including the OAuth token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The API base address to use, honouring <see cref="BaseUrl"/> when present.</summary>
    public string ResolvedBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var isLive = Environment is not null &&
                     (Environment.Equals("live", System.StringComparison.OrdinalIgnoreCase) ||
                      Environment.Equals("production", System.StringComparison.OrdinalIgnoreCase));

        return isLive
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
