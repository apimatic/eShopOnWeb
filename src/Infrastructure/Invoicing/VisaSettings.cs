namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa (CyberSource) invoicing integration, bound from the
/// <c>Visa</c> configuration section.
///
/// The credential values (<see cref="MerchantId"/>, <see cref="KeyId"/>,
/// <see cref="SecretKey"/>) are supplied at runtime from the environment via .NET
/// user-secrets and are never written into the repository. <see cref="BaseUrl"/> is
/// the provider base address; every call the integration makes is routed through it.
/// </summary>
public class VisaSettings
{
    public const string CONFIG_SECTION = "Visa";

    /// <summary>The provider base address (for example https://apitest.cybersource.com).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The currency the provider account bills in (this account bills in USD).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Merchant id (from the VISA_MERCHANT_ID environment variable).</summary>
    public string? MerchantId { get; set; }

    /// <summary>Shared-secret key id (from the VISA_KEY_ID environment variable).</summary>
    public string? KeyId { get; set; }

    /// <summary>Shared secret (from the VISA_SECRET_KEY environment variable). Never logged or returned.</summary>
    public string? SecretKey { get; set; }
}
