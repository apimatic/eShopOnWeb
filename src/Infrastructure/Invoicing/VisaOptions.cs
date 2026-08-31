namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa/CyberSource invoicing integration. Bound from the <c>Visa</c>
/// configuration section. Credentials are supplied out-of-band (environment / user-secrets) and
/// are never written into the repository; <see cref="BaseUrl"/> is the base address every provider
/// call is routed through and comes from configuration so the same build can run against a
/// different account and a different address.
/// </summary>
public class VisaOptions
{
    public const string SectionName = "Visa";

    /// <summary>The provider base address. Every call this integration makes to Visa is routed through it.</summary>
    public string? BaseUrl { get; set; }

    public string? MerchantId { get; set; }

    public string? KeyId { get; set; }

    /// <summary>The shared secret. Secret material — never logged, never returned, never written to a file.</summary>
    public string? SecretKey { get; set; }
}
