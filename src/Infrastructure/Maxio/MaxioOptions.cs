namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing (Billing API) integration.
/// Bound from the "Maxio" configuration section. Values are supplied via
/// user-secrets or environment variables and are never committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Billing API key. Used as the HTTP Basic username ("X" is the password).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maxio site subdomain, e.g. "acme" for https://acme.chargify.com.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plan catalog.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional full API base address override (e.g. an API Gateway URL).
    /// When set, it is used verbatim instead of the subdomain-derived URL.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public bool HasBaseAddress => !string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain);

    public bool HasProductFamily => !string.IsNullOrWhiteSpace(ProductFamilyHandle);
}
