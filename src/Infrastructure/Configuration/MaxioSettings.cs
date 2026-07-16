namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors how <c>CatalogSettings</c> is
/// bound elsewhere in eShopOnWeb). Bound from the <c>Maxio</c> configuration section — user-secrets for
/// <see cref="ApiKey"/>, and any of appsettings/user-secrets/environment variables/launchSettings for the
/// rest (see plan §5). Nothing here is hardcoded; nothing here is committed with a real value.
/// </summary>
public class MaxioSettings
{
    /// <summary>Maxio Advanced Billing API key. Sent as the HTTP Basic auth username (password is literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "apimatic-hackathon". Used to derive the default host when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region — "US" (default) or "EU". This is a separate axis from <see cref="BaseUrl"/> (§2.3).</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL (e.g. a local mock server for tests, or a
    /// specific host for a dev tenant). When set, it wins verbatim over the <see cref="Subdomain"/>-derived
    /// host — this is a hard requirement (§2.3): implementers must never silently ignore this value.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int? ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int? DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int? AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int? MeteredComponentId { get; set; }
}
