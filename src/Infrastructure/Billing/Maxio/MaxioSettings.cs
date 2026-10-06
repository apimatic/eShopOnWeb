using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Strongly typed view of the <c>Maxio:</c> configuration section.
/// Values are supplied by the environment (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN,
/// MAXIO_DEFAULT_PRODUCT_FAMILY) or by user-secrets - never committed to the repository.
/// </summary>
public class MaxioSettings
{
    public const string SECTION_NAME = "Maxio";

    /// <summary>API key used as the Basic auth username (Maxio sends the literal "x" as password).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Advanced Billing site subdomain, e.g. "cp-exp-1".</summary>
    public string? Subdomain { get; set; }

    /// <summary>Product family handle that holds the subscribable plans, e.g. "eshop-subscribe".</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set it is used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/> (useful for EU sites or a gateway/proxy).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Resolves the API base address.
    /// Per the OpenAPI spec the production US server template is https://{"{"}site{"}"}.chargify.com,
    /// so when no explicit override is configured the address is derived from the subdomain.
    /// </summary>
    public Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return new Uri(BaseUrl.Trim() + (BaseUrl.TrimEnd().EndsWith("/") ? string.Empty : "/"), UriKind.Absolute);

        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException(
                $"Maxio billing is not configured. Set '{SECTION_NAME}:BaseUrl' or '{SECTION_NAME}:Subdomain'.");

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    public bool IsUsable()
        => !string.IsNullOrWhiteSpace(ApiKey)
           && (!string.IsNullOrWhiteSpace(BaseUrl) || !string.IsNullOrWhiteSpace(Subdomain))
           && !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    /// <summary>Basic auth header value: api key as username, literal "x" as password.</summary>
    public string BasicAuthHeaderValue()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException($"Maxio billing is not configured. Missing '{SECTION_NAME}:ApiKey'.");

        return "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{ApiKey}:x"));
    }
}
