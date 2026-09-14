using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the <c>Maxio</c>
/// configuration section. No value is hard-coded so the same build can run against a
/// different Maxio site/catalog.
/// </summary>
public sealed class MaxioBillingOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (Basic auth username, password is "X").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the target Maxio site, e.g. <c>cp-exp-8</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that holds the subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim instead of deriving
    /// one from the subdomain/environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maxio region used to derive the base address: <c>US</c> or <c>EU</c>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Resolves the API base address. Prefers <see cref="BaseUrl"/> when provided, otherwise
    /// derives the documented host for the region from <see cref="Subdomain"/>.
    /// </summary>
    public string ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioBillingConfigurationException(
                $"{SectionName}:Subdomain is not configured. Set the MAXIO_SITE_SUBDOMAIN environment variable (or Maxio:BaseUrl).");
        }

        string region = string.IsNullOrWhiteSpace(Environment) ? "US" : Environment.Trim().ToUpperInvariant();
        return region switch
        {
            "US" => $"https://{Subdomain}.chargify.com",
            "EU" => $"https://{Subdomain}.ebilling.maxio.com",
            _ => throw new MaxioBillingConfigurationException(
                $"Maxio region '{Environment}' is not supported. Supported values are US and EU, or set {SectionName}:BaseUrl explicitly.")
        };
    }
}
