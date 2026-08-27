namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the "PayPal" configuration section. Secrets are supplied via user-secrets or
/// environment variables; none of these values are hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live".</summary>
    public string? Environment { get; set; }
    public string? Currency { get; set; }

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every
    /// PayPal call, including the token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://api-m.sandbox.paypal.com",
            "live" or "production" => "https://api-m.paypal.com",
            _ => throw new System.InvalidOperationException(
                $"PayPal:Environment must be 'sandbox' or 'live' unless PayPal:BaseUrl is set (was '{Environment}').")
        };
    }
}
