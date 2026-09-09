using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing configuration, bound from the "Maxio" configuration
/// section. Values are supplied via .NET user-secrets / environment variables and are never
/// stored in the repository.
/// </summary>
public class MaxioSettings
{
    public const string ConfigSection = "Maxio";

    /// <summary>Maxio API key, used as the Basic-auth username (password is the literal "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "cp-exp-7". Used to derive the API base URL.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim; otherwise the base URL is
    /// derived from <see cref="Subdomain"/> as https://{subdomain}.chargify.com.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How Maxio should collect payment. Defaults to "remittance" (invoice-based) so enrollment
    /// works for plans that do not require a payment method — no card capture / 3-DS.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Resolves the effective API base URL (always ending in a single trailing slash).</summary>
    public Uri ResolveBaseUri()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain}.chargify.com";

        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    /// <summary>Throws when required settings are missing, so misconfiguration fails fast at startup.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"Missing '{ConfigSection}:ApiKey'. Provide it via user-secrets (from the MAXIO_API_KEY environment variable).");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Missing '{ConfigSection}:Subdomain' (from MAXIO_SITE_SUBDOMAIN) and no '{ConfigSection}:BaseUrl' override was supplied.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Missing '{ConfigSection}:ProductFamilyHandle' (from MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
    }
}
