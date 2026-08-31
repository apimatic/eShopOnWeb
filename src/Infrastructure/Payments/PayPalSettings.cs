namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Bound from the "PayPal" configuration section. Values arrive from
/// environment variables / user-secrets — never from files in the repo.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";
    public const string HTTP_CLIENT_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>Optional override used verbatim as the API base address for every PayPal call, including the token request.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Emits method/URI/status for every PayPal call. Bodies are never logged.</summary>
    public bool LogHttp { get; set; }
}
