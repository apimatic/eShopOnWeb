namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa/CyberSource invoicing integration, bound from the "Visa" configuration
/// section. The credentials are supplied via configuration (user-secrets / environment) and are never
/// written into source or logged; <see cref="BaseUrl"/> is the address every provider call is routed
/// through and is used verbatim.
/// </summary>
public class VisaSettings
{
    public const string ConfigSection = "Visa";

    /// <summary>The provider base address. Every call the integration makes is routed through this, verbatim.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>CyberSource merchant/org id. Bridged to the VISA_MERCHANT_ID the signing hook reads.</summary>
    public string? MerchantId { get; set; }

    /// <summary>CyberSource key id. Bridged to the VISA_KEY_ID the signing hook reads.</summary>
    public string? KeyId { get; set; }

    /// <summary>CyberSource shared secret (base64). Bridged to the VISA_SECRET_KEY the signing hook reads. Never logged.</summary>
    public string? SecretKey { get; set; }
}
