using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa/CyberSource invoicing integration, bound from the "Visa" configuration
/// section. Credentials are supplied out of the repository (user-secrets / environment); only
/// <see cref="BaseUrl"/> carries a non-secret default.
/// </summary>
public class VisaSettings
{
    public const string SectionName = "Visa";

    /// <summary>
    /// The base address every provider call is routed through, verbatim. Required, and bound from
    /// configuration so the same build can run against a different address than the one shipped here.
    /// </summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string MerchantId { get; set; } = string.Empty;

    [Required]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The shared secret (base64). Never logged, never returned by an endpoint, never written to source.</summary>
    [Required]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Per-call budget applied to every provider call, in seconds.</summary>
    [Range(1, 600)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// A stable tag identifying this eShop deployment, stamped into the provider merchant-customer-id so
    /// reconciliation can recognise this deployment's own bills on the shared account. When left empty a
    /// per-process tag is generated (fine for the in-memory single-instance setup here; set a stable value
    /// in a shared-database deployment).
    /// </summary>
    public string? MerchantReferenceTag { get; set; }

    /// <summary>When true, logs the outgoing request line and response status/body for Visa calls (never secrets). Diagnostic only.</summary>
    public bool WireLog { get; set; }
}
