namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Values are supplied via
/// environment variables / user-secrets — never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base-address override. When set, it is used instead of
    /// deriving the base URL from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional payment collection method for new subscriptions
    /// (automatic | remittance | prepaid | invoice). Defaults to invoice so signup works
    /// without a card on file; automatic requires a payment method for paid plans.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
