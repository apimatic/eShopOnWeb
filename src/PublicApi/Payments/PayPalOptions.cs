namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl)) return BaseUrl.TrimEnd('/');
        if (Environment.Equals("Live", System.StringComparison.OrdinalIgnoreCase))
            return "https://api-m.paypal.com";
        if (Environment.Equals("Sandbox", System.StringComparison.OrdinalIgnoreCase))
            return "https://api-m.sandbox.paypal.com";
        throw new PaymentOperationException(503, "PayPal environment must be configured as Sandbox or Live.");
    }
}
