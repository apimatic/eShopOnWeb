namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Values are supplied via user-secrets / environment variables and are never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key. Used as the Basic-auth username per the OpenAPI specification
    /// (securitySchemes.BasicAuth: username is the API key, password is "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain, substituted into the spec's "{site}" server template.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Maxio Advanced Billing environment: "US" (https://{site}.chargify.com) or
    /// "EU" (https://{site}.ebilling.maxio.com), matching the spec's server groups.
    /// </summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Handle of the product family that holds the subscription plans to expose.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim override of the API base address. When set, it is used as-is instead of
    /// deriving a URL from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address, honoring the optional BaseUrl override.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return Environment.Trim().ToUpperInvariant() switch
        {
            "EU" => $"https://{Subdomain}.ebilling.maxio.com",
            _ => $"https://{Subdomain}.chargify.com"
        };
    }
}
