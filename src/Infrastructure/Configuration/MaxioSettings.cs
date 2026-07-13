namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors CatalogSettings'
/// use of the Options pattern). Bound from the "Maxio" configuration section: user-secrets for
/// the API key, appsettings/user-secrets/environment variables for everything else.
/// </summary>
public class MaxioSettings
{
    /// <summary>HTTP Basic Auth username; the password is the literal string "x". Never logged or committed.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "apimatic-hackathon". Used to derive the host when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The Maxio data-center region ("US" or "EU") - a separate axis from the deployment target controlled by <see cref="BaseUrl"/>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins verbatim over the
    /// Subdomain-derived host, so the identical build can target production, a dev/sandbox tenant,
    /// or a local mock server purely through configuration (plan.md §2.3).
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

    /// <summary>
    /// Resolves the outbound base URL. An explicit <see cref="BaseUrl"/> always wins (verbatim,
    /// trailing slash trimmed); otherwise the host is derived from <see cref="Subdomain"/> and the
    /// <see cref="Environment"/> region. This is the one place retargeting happens (plan.md §4.3).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var host = string.Equals(Environment, "EU", System.StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain}.ebilling.maxio.com"
            : $"{Subdomain}.chargify.com";

        return $"https://{host}";
    }
}
