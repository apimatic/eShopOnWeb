namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. Secrets are supplied at deployment time (user-secrets in
/// development, environment variables in production) — never committed to the repo.
/// Configuration keys (environment variable names in parentheses):
/// Maxio:ApiKey (MAXIO_API_KEY), Maxio:Subdomain (MAXIO_SITE_SUBDOMAIN),
/// Maxio:ProductFamilyHandle (MAXIO_DEFAULT_PRODUCT_FAMILY),
/// Maxio:BaseUrl (optional verbatim override of the API base address),
/// Maxio:Environment (MAXIO_ENVIRONMENT — "US" or "EU", default "US").
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";
    public const string ApiKeyEnvVar = "MAXIO_API_KEY";
    public const string SubdomainEnvVar = "MAXIO_SITE_SUBDOMAIN";
    public const string ProductFamilyHandleEnvVar = "MAXIO_DEFAULT_PRODUCT_FAMILY";
    public const string EnvironmentEnvVar = "MAXIO_ENVIRONMENT";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain; used to build the sandbox/production API host
    /// when <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The API handle of the product family that holds the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address override (e.g. a proxy or a self-hosted
    /// gateway). When set it replaces the URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// "US" (default) or "EU" — selects the Maxio hosting environment.
    /// </summary>
    public string? Environment { get; set; }
}