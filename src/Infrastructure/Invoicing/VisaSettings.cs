namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa (CyberSource) invoicing integration, bound from the <c>Visa</c>
/// configuration section. <see cref="BaseUrl"/> comes from configuration and every provider call is
/// routed through it. The credentials are supplied out of band (environment variables loaded into
/// user-secrets) and are never written to a source or settings file; <see cref="SecretKey"/> in
/// particular is never logged or returned.
/// </summary>
public class VisaSettings
{
    public const string ConfigSection = "Visa";

    /// <summary>Base address for every Visa call, e.g. <c>https://apitest.cybersource.com</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Merchant id (CyberSource organization id).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Shared-secret key id.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Shared-secret value. Secret — never logged, returned, or written to a file.</summary>
    public string SecretKey { get; set; } = string.Empty;
}
