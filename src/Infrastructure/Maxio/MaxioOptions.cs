using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the <c>Maxio</c> configuration
/// section; real values are supplied via user-secrets / environment variables and are never committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key used as the HTTP Basic username (password is a literal "X").
    /// Required.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain, e.g. "my-site" for https://my-site.chargify.com.
    /// Required unless <see cref="BaseUrl"/> overrides the base address entirely.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The product family handle whose products are offered as subscription plans.
    /// Required.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim override of the API base address (e.g. https://acme.chargify.com
    /// or a custom gateway host). When set, it is used instead of the address derived from
    /// <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The effective API base address with any trailing slash removed.
    /// </summary>
    public string EffectiveBaseUrl
    {
        get
        {
            var url = !string.IsNullOrWhiteSpace(BaseUrl) ? BaseUrl! : $"https://{Subdomain}.chargify.com";
            return url.TrimEnd('/');
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"Maxio configuration is incomplete: '{SectionName}:{nameof(ApiKey)}' is not set (configure it via user-secrets or the MAXIO_API_KEY secret).");
        }
        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio configuration is incomplete: '{SectionName}:{nameof(Subdomain)}' is not set.");
        }
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Maxio configuration is incomplete: '{SectionName}:{nameof(ProductFamilyHandle)}' is not set.");
        }
    }
}
