using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Configuration for the Visa/CyberSource billing integration, bound from the <c>Visa</c> section.
/// Credential <b>values</b> are loaded from configuration (user-secrets / environment) and never
/// live in a repository file. The provider base address is bound here so every call can be routed
/// through it and a different build can point at a different address.
/// </summary>
public sealed class VisaSettings
{
    public const string CONFIG_SECTION = "Visa";

    /// <summary>The merchant / organization id (CyberSource "Org ID").</summary>
    [Required(AllowEmptyStrings = false)]
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>The HTTP-Signature key id.</summary>
    [Required(AllowEmptyStrings = false)]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>The base64 shared secret used to sign requests. A secret — never logged or returned.</summary>
    [Required(AllowEmptyStrings = false)]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// The provider base address. When set, it is used verbatim in place of the SDK's default; every
    /// provider call is routed through it. Left empty, the SDK's built-in sandbox default applies.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The currency this account bills in. Fixed to USD for this account.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Opt-in wire diagnostics (method/path/status and error bodies). Never logs credentials. Default off.</summary>
    public bool LogRequests { get; set; }
}
