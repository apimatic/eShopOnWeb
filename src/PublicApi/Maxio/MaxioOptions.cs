namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Values are supplied
/// through the "Maxio" configuration section (user-secrets / environment variables);
/// no value is ever hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key, used as the HTTP Basic username ("X" is the password).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The Maxio site subdomain, used to derive the default base URL
    /// https://{Subdomain}.chargify.com.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family that holds the subscription plans exposed by
    /// this API (e.g. from MAXIO_DEFAULT_PRODUCT_FAMILY).
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional verbatim override of the API base address. When set it replaces
    /// the URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Payment collection method used when creating subscriptions. Defaults to
    /// "remittance" because these signups intentionally do not capture a payment
    /// method (the demo plans are configured without card capture / 3-DS);
    /// remittance instructs Maxio to invoice the customer instead of attempting
    /// an automatic card charge at signup. Set to "automatic" only when a
    /// payment profile is captured before subscribing.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Resolves the effective API base address: <see cref="BaseUrl"/> when set,
    /// otherwise https://{Subdomain}.chargify.com.
    /// </summary>
    public string GetEffectiveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Subdomain) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle);
}