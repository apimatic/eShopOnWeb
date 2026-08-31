namespace Microsoft.eShopWeb.Infrastructure.Invoicing.Visa;

/// <summary>
/// Configuration for the Visa (CyberSource) invoicing integration, bound from the "Visa"
/// configuration section. Credentials are supplied through configuration (loaded from
/// environment variables into user-secrets) and are never hard-coded.
/// </summary>
public class VisaSettings
{
    public const string SectionName = "Visa";

    /// <summary>
    /// The base address every call to Visa is routed through. When set it is used verbatim in
    /// place of any default the SDK would otherwise use, so the same build can run against a
    /// different address than the one configured here.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The merchant id (from the VISA_MERCHANT_ID environment variable).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>The shared-secret key id (from the VISA_KEY_ID environment variable).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The shared secret (from the VISA_SECRET_KEY environment variable). A secret; never logged or returned.</summary>
    public string SecretKey { get; set; } = string.Empty;
}
