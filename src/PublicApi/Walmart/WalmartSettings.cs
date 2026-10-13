using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Walmart;

/// <summary>
/// Settings for talking to Walmart Marketplace, bound from the <c>Walmart:</c> configuration section.
/// Values are never hard-coded: <see cref="ClientId"/> / <see cref="ClientSecret"/> come from the
/// <c>WALMART_CLIENT_ID</c> / <c>WALMART_CLIENT_SECRET</c> environment variables (loaded into .NET
/// user-secrets), and <see cref="BaseUrl"/> is an optional host override.
/// </summary>
public sealed class WalmartSettings
{
    public const string SectionName = "Walmart";

    /// <summary>Walmart client id (<c>Walmart:ClientId</c>, from <c>WALMART_CLIENT_ID</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string? ClientId { get; set; }

    /// <summary>Walmart client secret (<c>Walmart:ClientSecret</c>, from <c>WALMART_CLIENT_SECRET</c>). Never logged or returned.</summary>
    [Required(AllowEmptyStrings = false)]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Optional base-URL override in the form <c>scheme://host[:port]</c>. When set, it replaces the
    /// scheme, host and port of every Walmart call (including obtaining access tokens); everything after
    /// the host — the contract's base path and route — is left exactly as the SDK forms it.
    /// </summary>
    public string? BaseUrl { get; set; }
}
