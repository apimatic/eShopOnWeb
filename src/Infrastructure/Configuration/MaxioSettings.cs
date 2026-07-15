namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options bound from the "Maxio" configuration section (user-secrets / env vars —
/// see plan.md §5). Entities are resolved by handle at startup rather than trusting a
/// hard-coded numeric id, since Maxio reassigns ids whenever the sandbox is reseeded.
/// </summary>
public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound target server. When set, it wins over
    /// the Subdomain-derived host — see plan.md §2.3/§4.3. Leave empty to derive the host
    /// from <see cref="Subdomain"/> + <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string DefaultProductHandle { get; set; } = string.Empty;
    public string AlternateProductHandle { get; set; } = string.Empty;
    public string MeteredComponentHandle { get; set; } = string.Empty;
}
