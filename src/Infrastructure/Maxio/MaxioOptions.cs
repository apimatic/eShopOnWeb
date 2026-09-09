using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing (Billing API) integration.
/// Bound from the "Maxio" configuration section; values come from user secrets
/// / environment variables and must never be committed to the repository.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>The Maxio API key. Sent as the Basic-auth username ("x" is the password).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "your-site" for your-site.chargify.com.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of deriving
    /// one from <see cref="Subdomain"/> (e.g. for other Maxio environments/regions).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The handle of the product family that contains the subscription plans
    /// offered by eShopOnWeb.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override of the Billing API subscription
    /// payment_collection_method ("automatic", "remittance", "prepaid" or "invoice").
    /// When unset, enrollment uses manual (remittance / invoice) collection so a
    /// signup can succeed without a payment profile, falling back across site
    /// architectures automatically.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// The base address of the Billing API: <see cref="BaseUrl"/> when set, otherwise
    /// derived from the site subdomain. Maxio US/EU sites are served from
    /// https://{subdomain}.chargify.com.
    /// </summary>
    public Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio configuration is incomplete: set '{SectionName}:{nameof(BaseUrl)}' or '{SectionName}:{nameof(Subdomain)}'.");
        }

        return new Uri($"https://{Subdomain.Trim().ToLowerInvariant()}.chargify.com/", UriKind.Absolute);
    }

    public void ValidateRequiredSettings()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                $"Maxio configuration is incomplete: '{SectionName}:{nameof(ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Maxio configuration is incomplete: '{SectionName}:{nameof(ProductFamilyHandle)}' is required.");
        }
    }
}
