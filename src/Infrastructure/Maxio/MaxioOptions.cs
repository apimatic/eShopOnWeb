using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed view of the <c>Maxio:</c> configuration section.
/// Bound in the composition root from configuration keys:
/// <c>Maxio:ApiKey</c>, <c>Maxio:Subdomain</c>, <c>Maxio:ProductFamilyHandle</c>, <c>Maxio:BaseUrl</c>.
/// No values are hard-coded so the same build can target a different Maxio site / catalog.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Advanced Billing site API key (used as the Basic-auth username).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Advanced Billing site subdomain, used to derive the API host when BaseUrl is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that contains the plans to offer.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> (e.g. to target the EU host
    /// https://{site}.ebilling.maxio.com).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address (without a trailing slash).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured. Provide either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        // Advanced Billing (US) hosts every site at https://{subdomain}.chargify.com.
        return $"https://{Subdomain.Trim()}.chargify.com";
    }
}
