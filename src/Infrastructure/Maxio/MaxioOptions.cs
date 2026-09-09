namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing (Billing API) connection.
/// Bound from the "Maxio" configuration section; values are supplied via
/// user-secrets / environment variables and never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The site API key. Used as the Basic-auth username (password is "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain (e.g. "cp-exp-4" for cp-exp-4.chargify.com).
    /// Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional full API base URL override (e.g. "https://my-site.chargify.com").
    /// When set it is used verbatim instead of the URL derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string EffectiveBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl)
        ? BaseUrl!.TrimEnd('/')
        : $"https://{Subdomain.Trim().ToLowerInvariant()}.chargify.com";
}
