using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing API. Bound from the "Maxio" configuration
/// section; values are supplied via user-secrets (in Development) or environment variables.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key used as the Basic-auth username.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The handle of the product family that holds the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional full API base URL override (e.g. "https://mysite.chargify.com").
    /// When set, it is used verbatim instead of deriving the URL from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The Maxio environment ("US" or "EU"); only used to derive the base URL when
    /// <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Resolves the API base URL. <see cref="BaseUrl"/> wins when set; otherwise the URL is
    /// derived from the subdomain per environment (US sites are *.chargify.com, EU sites
    /// are *.ebilling.maxio.com).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        GuardConfigured(Subdomain, nameof(Subdomain));
        var environment = (Environment ?? "US").Trim().ToUpperInvariant();
        return environment switch
        {
            "EU" => $"https://{Subdomain.TrimEnd('/')}.ebilling.maxio.com",
            _ => $"https://{Subdomain.TrimEnd('/')}.chargify.com"
        };
    }

    public void Validate()
    {
        GuardConfigured(ApiKey, nameof(ApiKey));
        GuardConfigured(ProductFamilyHandle, nameof(ProductFamilyHandle));
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            GuardConfigured(Subdomain, nameof(Subdomain));
        }
    }

    private static void GuardConfigured(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Maxio configuration value '{MaxioOptions.SectionName}:{name}' is missing. " +
                "Set it via user-secrets or environment variables.");
        }
    }
}