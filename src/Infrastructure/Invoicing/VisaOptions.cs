namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa (CyberSource) invoicing integration, bound from the "Visa"
/// configuration section.
///
/// Credentials (<see cref="MerchantId"/>, <see cref="KeyId"/>, <see cref="SecretKey"/>) are
/// supplied out of band via .NET user-secrets / environment variables and never committed to
/// the repository. <see cref="BaseUrl"/> is bound from configuration and used verbatim as the
/// base address for every provider call, so the same build can run against a different Visa
/// account and address without code changes.
/// </summary>
public class VisaOptions
{
    public const string SectionName = "Visa";

    /// <summary>CyberSource merchant id (from VISA_MERCHANT_ID).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Shared-secret key id (from VISA_KEY_ID).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Base64 shared secret (from VISA_SECRET_KEY). Never logged or returned.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// The base address of the provider. Every call this integration makes to Visa is routed
    /// through this address. Example: https://apitest.cybersource.com
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The currency every bill is raised in for this account.</summary>
    public string Currency { get; set; } = "USD";
}
