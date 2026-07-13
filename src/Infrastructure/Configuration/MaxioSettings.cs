namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors <c>CatalogSettings</c>'s
/// Options-binding pattern). Bound from the "Maxio" configuration section: user-secrets for
/// <see cref="ApiKey"/>, and <c>appsettings.json</c> / environment variables / user-secrets for
/// everything else. Never hardcode any of these values in source.
/// </summary>
public class MaxioSettings
{
    /// <summary>Maxio/Chargify API key. Sent as the HTTP Basic auth username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the host when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region: "US" (default) or "EU". This is NOT the deployment target — see <see cref="BaseUrl"/>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL (the deployment target: production, a
    /// dev/sandbox tenant, or a local mock server). When set, it is honored verbatim and wins over the
    /// <see cref="Subdomain"/>-derived host. Leave empty to use the derived host.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }

    public bool IsEuEnvironment => string.Equals(Environment, "EU", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit <see cref="BaseUrl"/> if present (honored verbatim), otherwise the host derived from
    /// <see cref="Subdomain"/> and the region <see cref="Environment"/> (§2.3). Used to set the typed
    /// HttpClient's BaseAddress; the actual per-request routing is driven by the equivalent resolution
    /// applied to the Maxio SDK client's <c>Server</c> options in <c>MaxioBillingClient</c>.
    /// </summary>
    public System.Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new System.Uri(BaseUrl, System.UriKind.Absolute);
        }

        var host = IsEuEnvironment ? $"{Subdomain}.ebilling.maxio.com" : $"{Subdomain}.chargify.com";
        return new System.Uri($"https://{host}");
    }
}
