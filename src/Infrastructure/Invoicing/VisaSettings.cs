namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa / CyberSource invoicing integration, bound from the "Visa" section.
/// None of these values are ever hard-coded: the credentials are read from configuration
/// (user-secrets / environment), and <see cref="BaseUrl"/> is bound so the same build can be pointed
/// at a different provider address without a code change.
/// </summary>
public class VisaSettings
{
    public const string SectionName = "Visa";

    /// <summary>
    /// The base address every provider call is routed through, used verbatim. When set it replaces
    /// whatever default host the SDK would otherwise use.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The CyberSource merchant id.</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>The shared-secret key id.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The shared secret. Secret material: never logged, never returned, never persisted to source.</summary>
    public string SecretKey { get; set; } = string.Empty;
}
