namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa / CyberSource invoicing integration, bound from the "Visa" configuration
/// section.
///
/// <para><see cref="BaseUrl"/> is supplied through configuration (appsettings) and every call this
/// integration makes to the provider is routed through it. The credential values (<see cref="MerchantId"/>,
/// <see cref="KeyId"/>, <see cref="SecretKey"/>) are secrets and are supplied out-of-band via .NET
/// user-secrets / environment variables — never hard-coded and never written into the repository.</para>
/// </summary>
public class VisaSettings
{
    public const string ConfigSection = "Visa";

    /// <summary>
    /// The provider base address (e.g. <c>https://apitest.cybersource.com</c>). Used verbatim as the
    /// base address for every provider call; the same build can run against a different address by
    /// changing this value alone.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>CyberSource merchant id. Secret — provided via user-secrets, not the repository.</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>CyberSource shared-secret key id. Secret — provided via user-secrets, not the repository.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>CyberSource shared-secret key. Secret — provided via user-secrets, never logged or returned.</summary>
    public string SecretKey { get; set; } = string.Empty;
}
