using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Options bound from the <c>Maxio:</c> configuration section.
///
/// The API key and site subdomain are injected from <c>MAXIO_API_KEY</c> and
/// <c>MAXIO_SITE_SUBDOMAIN</c> at deploy time (via user-secrets / environment) and are never
/// stored in the repository. <c>BaseUrl</c> is an optional override; when it is set it is used
/// verbatim as the API base address instead of deriving one from the subdomain.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public const string PaymentCollectionMethodRemittance = "remittance";

    /// <summary>Maxio Advanced Billing API key (Basic auth user name).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain used to build the default API base address.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The API handle of the product family that holds the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim instead of the
    /// address derived from <see cref="Subdomain"/>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets the API base address. Prefers <see cref="BaseUrl"/> when set; otherwise derives the
    /// standard Advanced Billing host from the site subdomain
    /// (<c>https://{subdomain}.chargify.com</c>).
    /// </summary>
    public string ApiBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl.TrimEnd('/');
}
