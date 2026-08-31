using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa/CyberSource invoicing integration. Bound from the <c>Visa</c> section.
/// <para>
/// <see cref="BaseUrl"/> is the only value required at startup; it is used verbatim as the base address
/// for every provider call. The three credentials are secrets — they are never written into any file in
/// the repository, are loaded from user-secrets / environment variables, and are validated by the SDK's
/// HTTP Signature hook when the client is constructed (which names any missing one).
/// </para>
/// </summary>
public class VisaSettings
{
    public const string SectionName = "Visa";

    /// <summary>The provider base address. Every call this integration makes is routed through it verbatim.</summary>
    [Required(AllowEmptyStrings = false)]
    public string BaseUrl { get; set; } = string.Empty;

    public string MerchantId { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    /// <summary>Secret shared key (base64). Never logged, returned by an endpoint, or written to a file.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Per-call deadline, in seconds, applied to each provider call (and each retry attempt).</summary>
    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; set; } = 30;
}
