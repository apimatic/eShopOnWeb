namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the
/// "Maxio" configuration section. Values come from user-secrets /
/// environment — never hard-coded — so the same build can run against any
/// Maxio site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (sent as the Basic-auth username).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain; the API host is derived from it.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address (scheme + host). When set, it
    /// replaces the address derived from <see cref="Subdomain"/>; when empty,
    /// the address is derived from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}
