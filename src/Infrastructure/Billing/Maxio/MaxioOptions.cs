using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Values are supplied via user-secrets/environment (never committed to the repo).
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key. Used as the Basic auth username; the password is "x" per the OpenAPI spec.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain used to derive the API base URL (e.g. "cp-exp-3").
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The hosting environment for the Maxio site ("US" or "EU") as defined by the
    /// OpenAPI server configuration. Defaults to US.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// The handle of the product family containing the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override: when set, used verbatim as the API base address instead of
    /// deriving one from the subdomain/environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base URL per the OpenAPI server configuration:
    /// BaseUrl override > https://{subdomain}.chargify.com (US) > https://{subdomain}.ebilling.maxio.com (EU)
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio site is not configured: set Maxio:BaseUrl or Maxio:Subdomain.");
        }

        var host = string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";
        return $"https://{Subdomain.Trim()}.{host}";
    }
}

/// <summary>
/// Thrown when the Maxio integration is misconfigured (missing credentials, base URL, etc.)
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}