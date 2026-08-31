namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa (CyberSource) invoicing integration, bound from the <c>Visa</c>
/// configuration section. Credentials are supplied out of band (environment variables loaded into
/// .NET user-secrets) and never written into the repository.
/// </summary>
public class VisaOptions
{
    public const string SectionName = "Visa";

    /// <summary>
    /// The base address every provider call is routed through. Bound from configuration and used
    /// verbatim as the provider host in place of any default, so the same build can run against a
    /// different Visa address than the sandbox.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Sandbox merchant id (from the <c>VISA_MERCHANT_ID</c> environment variable).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Shared-secret key id (from the <c>VISA_KEY_ID</c> environment variable).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Shared-secret key (from the <c>VISA_SECRET_KEY</c> environment variable). This is a secret:
    /// it is never logged, never returned by an endpoint, and never written into a source file.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;
}
