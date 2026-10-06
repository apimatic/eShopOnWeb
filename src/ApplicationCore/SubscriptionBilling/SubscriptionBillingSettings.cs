using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio"
/// configuration section. Values arrive from environment variables / .NET user
/// secrets and must never be committed to the repository.
/// </summary>
public class SubscriptionBillingSettings
{
    /// <summary>Configuration section the values are bound from.</summary>
    public const string SECTION_NAME = "Maxio";

    /// <summary>API key (HTTP Basic username; the password is fixed to "X" by Maxio).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain; used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family that contains the plans offered to shoppers.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> (e.g. for EU-hosted sites).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Effective API root. Prefer <see cref="BaseUrl"/> when provided; otherwise derive
    /// https://{Subdomain}.chargify.com, which is the documented URL form for the US environment.
    /// Never echoes secret values in exceptions.
    /// </summary>
    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl.TrimEnd('/'), UriKind.Absolute, out var overriddenUri) ||
                (overriddenUri.Scheme != Uri.UriSchemeHttps && overriddenUri.Scheme != Uri.UriSchemeHttp))
            {
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Configuration,
                    "Maxio:BaseUrl is set but is not a valid absolute http/https URL.");
            }

            return overriddenUri.ToString().TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Configuration,
                "Maxio billing is not configured. Set the Maxio:Subdomain (or Maxio:BaseUrl) and Maxio:ApiKey configuration keys.");
        }

        return $"https://{Subdomain.Trim()}.chargify.com";
    }

    /// <summary>Validates the settings required before any API call is attempted.</summary>
    public void EnsureConfigured()
    {
        GetApiBaseUrl();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Configuration,
                "Maxio billing is not configured. Set the Maxio:ApiKey configuration key.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Configuration,
                "Maxio billing is not configured. Set the Maxio:ProductFamilyHandle configuration key.");
        }
    }
}
