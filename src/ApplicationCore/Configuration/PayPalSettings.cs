namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed PayPal settings bound from the "PayPal:" configuration section.
/// Values are never hard-coded — they are supplied via user-secrets / environment.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "production"/"live".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for all amounts (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base URL override. When set, it is used verbatim as the API base
    /// address for every PayPal call (including the OAuth token request). When
    /// empty, the base URL is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Effective API base address, honouring the BaseUrl override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var isProduction = Environment is not null &&
            (Environment.Equals("production", System.StringComparison.OrdinalIgnoreCase) ||
             Environment.Equals("live", System.StringComparison.OrdinalIgnoreCase));

        return isProduction
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
