using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// Binds the <c>Walmart:</c> configuration section. Values are supplied by configuration
/// (environment variables / user-secrets) and are never hard-coded. The client secret is a
/// secret: it is never logged, returned, or written to a source file.
/// </summary>
public class WalmartSettings
{
    public const string ConfigSection = "Walmart";

    /// <summary>Walmart seller client id (<c>Walmart:ClientId</c>, from WALMART_CLIENT_ID).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Walmart seller client secret (<c>Walmart:ClientSecret</c>, from WALMART_CLIENT_SECRET).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override, in the form <c>scheme://host[:port]</c>. When set, it replaces
    /// the scheme, host and port of every Walmart call (API groups and token acquisition alike);
    /// everything after the host — including the contract's base path — is preserved.
    /// </summary>
    public string? BaseUrl { get; set; }
}
