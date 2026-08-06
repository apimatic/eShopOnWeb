namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal configuration, bound from the "PayPal" configuration section. Values come from
/// environment / user-secrets and are never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Always "sandbox" for this task.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Optional explicit base URL. When set it is used verbatim.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The API base address: the explicit <see cref="BaseUrl"/> when provided, otherwise the
    /// address derived from <see cref="Environment"/>. Only sandbox is supported.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        // Per the PayPal OpenAPI specs the sandbox server is https://api-m.sandbox.paypal.com
        return "https://api-m.sandbox.paypal.com";
    }
}
