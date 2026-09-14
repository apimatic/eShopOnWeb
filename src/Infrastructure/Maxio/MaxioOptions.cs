using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio (Advanced Billing) integration.
/// Bound from the <c>Maxio</c> configuration section using exactly these keys:
/// <c>Maxio:ApiKey</c>, <c>Maxio:Subdomain</c>, <c>Maxio:Environment</c>,
/// <c>Maxio:ProductFamilyHandle</c> and the optional <c>Maxio:BaseUrl</c> override.
/// No secret value is ever committed to the repository; values are supplied through
/// environment variables or .NET user-secrets at run time.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public const string EnvironmentUnitedStates = "US";
    public const string EnvironmentEuropeanUnion = "EU";

    /// <summary>The Maxio/Advanced Billing API key (Basic auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain (e.g. <c>cp-exp-2</c>).</summary>
    public string? Subdomain { get; set; }

    /// <summary>The Advanced Billing environment (<c>US</c> or <c>EU</c>).</summary>
    public string? Environment { get; set; }

    /// <summary>The handle of the Maxio product family that hosts the subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim;
    /// otherwise the base address is derived from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsEnvironmentEuropeanUnion =>
        string.Equals(Environment, EnvironmentEuropeanUnion, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the Advanced Billing API base address (without a trailing slash).
    /// Honors the <see cref="BaseUrl"/> override when present.
    /// </summary>
    public string ResolveApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: 'Maxio:Subdomain' is required when 'Maxio:BaseUrl' is not provided.");
        }

        string host = IsEnvironmentEuropeanUnion ? "ebilling.maxio.com" : "chargify.com";
        return $"https://{Subdomain.Trim()}.{host}";
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> when the options required to talk to Maxio
    /// are missing, so misconfiguration fails fast at startup.
    /// </summary>
    public void Validate()
    {
        var missing = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey)) missing.Add(nameof(ApiKey) + " (from MAXIO_API_KEY)");
        if (string.IsNullOrWhiteSpace(Subdomain)) missing.Add(nameof(Subdomain) + " (from MAXIO_SITE_SUBDOMAIN)");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) missing.Add(nameof(ProductFamilyHandle) + " (from MAXIO_DEFAULT_PRODUCT_FAMILY)");

        if (missing.Count == 0)
        {
            ResolveApiBaseUrl();
            return;
        }

        throw new InvalidOperationException(
            "Maxio integration is not configured. Supply the following settings through the 'Maxio' " +
            "configuration section (for example via the MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / " +
            "MAXIO_DEFAULT_PRODUCT_FAMILY environment variables, or .NET user-secrets): " +
            string.Join(", ", missing) + ".");
    }
}
