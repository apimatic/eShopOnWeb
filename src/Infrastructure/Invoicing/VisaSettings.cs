namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa (CyberSource) invoicing integration. Bound from the <c>Visa</c>
/// configuration section. The credential values are supplied out of band (environment variables loaded
/// into .NET user-secrets) and never live in the repository; <see cref="SecretKey"/> in particular is a
/// secret and is never logged or returned by any endpoint.
/// </summary>
public class VisaSettings
{
    public const string CONFIG_SECTION = "Visa";

    /// <summary>
    /// The base address every call to the provider is routed through. When set it is used verbatim as
    /// the provider host in place of any SDK default; the same build can therefore run against a
    /// different address than the sandbox one.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The merchant id (from <c>VISA_MERCHANT_ID</c>).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>The shared-secret key id (from <c>VISA_KEY_ID</c>).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The shared secret (from <c>VISA_SECRET_KEY</c>). Secret — never logged or returned.</summary>
    public string SecretKey { get; set; } = string.Empty;
}
