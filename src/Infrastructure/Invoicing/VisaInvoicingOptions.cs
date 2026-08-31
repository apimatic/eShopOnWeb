namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa (CyberSource) invoicing integration. Bound from the <c>Visa</c>
/// configuration section. The credential values are supplied out-of-band (environment / user-secrets)
/// and are never written into the repository. <see cref="BaseUrl"/> is bound from configuration and is
/// the single source of the provider base address — every call the integration makes is routed through it.
/// </summary>
public class VisaInvoicingOptions
{
    public const string SectionName = "Visa";

    /// <summary>The provider base address. Every provider call is routed through this, verbatim.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string MerchantId { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    /// <summary>Shared secret. Never logged, never returned by an endpoint, never persisted in the repo.</summary>
    public string SecretKey { get; set; } = string.Empty;
}
