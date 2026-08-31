namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa/CyberSource invoicing integration, bound from the <c>Visa</c> section.
/// Credential values are loaded from configuration (user-secrets / environment) and are never written to
/// any file in the repository.
/// </summary>
public class VisaSettings
{
    public const string SectionName = "Visa";

    /// <summary>
    /// The base address every provider call is routed through. When set it is used verbatim, in place of the
    /// SDK's built-in default. Bound from <c>Visa:BaseUrl</c>; leave unset to use the SDK default sandbox host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>CyberSource merchant / organization id (EBC "Org ID"). Bridged to <c>VISA_MERCHANT_ID</c>.</summary>
    public string? MerchantId { get; set; }

    /// <summary>HTTP-signature key id (EBC "Key"). Bridged to <c>VISA_KEY_ID</c>.</summary>
    public string? KeyId { get; set; }

    /// <summary>HTTP-signature shared secret (EBC "Shared Secret Key", base64). Bridged to <c>VISA_SECRET_KEY</c>. Never logged or returned.</summary>
    public string? SecretKey { get; set; }

    /// <summary>The whole-call budget, in seconds, applied to every provider call.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When true, logs each provider request line and (truncated) response body at Debug — a diagnostic aid
    /// for verifying a new integration on the wire. Off by default. Never logs the secret or the signature.
    /// </summary>
    public bool LogWire { get; set; }
}
