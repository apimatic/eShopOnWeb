namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Values must come from user
/// secrets or environment variables - never from committed files.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key (used as the HTTP Basic username).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain, e.g. "acme" for https://acme.chargify.com.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address (e.g. https://acme.chargify.com).
    /// When set, it is used verbatim instead of deriving the host from Subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional Advanced Billing payment collection method used when creating
    /// subscriptions. Valid values for Relationship Invoicing sites:
    /// "remittance", "automatic", "prepaid". Defaults to "remittance" so a
    /// subscription can be created without a payment method on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }
}