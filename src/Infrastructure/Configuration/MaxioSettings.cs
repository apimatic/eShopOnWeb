namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options bound from the "Maxio" configuration section (mirrors <c>CatalogSettings</c>).
/// <see cref="ApiKey"/> is the only sensitive value here and is expected to arrive via .NET
/// user-secrets or an environment variable — never via appsettings.json or source.
/// </summary>
public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region ("US" or "EU") — orthogonal to <see cref="BaseUrl"/>, which
    /// picks the deployment target (prod/dev/mock).</summary>
    public string Environment { get; set; } = "US";

    /// <summary>Explicit outbound base URL override. When set, it wins verbatim over the
    /// <see cref="Subdomain"/>-derived host (§2.3) — this is the knob that lets the same build target
    /// production, a dev/sandbox tenant, or a local mock server through configuration alone.</summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }

    /// <summary>The effective outbound host: the explicit <see cref="BaseUrl"/> override if set,
    /// otherwise the host derived from <see cref="Subdomain"/>. For diagnostics/logging only — the
    /// actual SDK routing is configured from these same two values in <c>MaxioBillingClient</c>.</summary>
    public string ResolveEffectiveBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl) ? $"https://{Subdomain}.chargify.com" : BaseUrl!;
}
