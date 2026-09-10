using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration. Bound from the <c>Maxio</c> configuration
/// section. Values are supplied at runtime (via .NET user-secrets / environment) and are never
/// committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Used as the username in HTTP Basic auth (password is the literal "X").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (e.g. <c>cp-exp-8</c>). Used to derive the API base URL.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL is derived
    /// from <see cref="Subdomain"/> as <c>https://{subdomain}.chargify.com</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base address, honoring <see cref="BaseUrl"/> when provided.</summary>
    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.EndsWith("/", StringComparison.Ordinal) ? BaseUrl : BaseUrl + "/");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }
}
