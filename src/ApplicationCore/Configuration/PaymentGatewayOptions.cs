namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the "PayPal" configuration section (keys: ClientId, ClientSecret,
/// Environment, Currency, BaseUrl). Values are supplied through environment variables
/// or user-secrets; none are committed to the repository.
/// </summary>
public class PaymentGatewayOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the base address for every
    /// processor call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        // Server URLs per the OpenAPI specs in api-specs/paypal (sandbox) and PayPal docs (live).
        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
