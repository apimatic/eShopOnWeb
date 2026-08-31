namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa (CyberSource) invoicing integration, bound from the <c>Visa</c>
/// configuration section.
///
/// <para><see cref="BaseUrl"/> is bound from configuration and every call the integration makes to
/// Visa is routed through it. Credentials are supplied out of band (environment variables loaded
/// into .NET user-secrets) and never live in source or appsettings.</para>
/// </summary>
public class VisaSettings
{
    public const string SectionName = "Visa";

    /// <summary>The base address of the provider. Used verbatim as the host for every provider call.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Provider merchant id (from the VISA_MERCHANT_ID environment variable / user-secrets).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Provider shared-secret key id (from the VISA_KEY_ID environment variable / user-secrets).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Provider shared secret (from the VISA_SECRET_KEY environment variable / user-secrets). Never logged or returned.</summary>
    public string SecretKey { get; set; } = string.Empty;
}
