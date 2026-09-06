using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Connection settings for Maxio Advanced Billing, bound from the <c>Maxio</c> configuration
/// section. No value here is ever baked into the build: the same binary has to run against a
/// different site and a different catalog by changing configuration only.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic username with the fixed password <c>x</c>, per the
    /// <c>BasicAuth</c> security scheme in maxio-spec/openapi.yaml.
    /// Supply via user-secrets or the environment; never commit it.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ApiKey is required.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The site subdomain, substituted into the <c>{site}</c> server variable of the specification
    /// server template <c>https://{site}.chargify.com</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:Subdomain is required.")]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family whose products are offered as subscription plans.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ProductFamilyHandle is required.")]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional absolute base URL override. When set it is used verbatim as the API base address
    /// instead of deriving one from <see cref="Subdomain"/>. This is also how a site on the EU
    /// environment is targeted (<c>https://{site}.ebilling.maxio.com</c>), and how tests point the
    /// client at a stub.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Per-request timeout, in seconds.</summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How many times a request may be retried after a throttling or transient server response.
    /// Only requests that are safe to repeat are retried.
    /// </summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential retry backoff, in milliseconds.</summary>
    [Range(0, 60000)]
    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>How long the site settings read from <c>GET /site.json</c> stay cached, in minutes.</summary>
    [Range(0, 1440)]
    public int SiteCacheMinutes { get; set; } = 15;

    /// <summary>
    /// Resolves the API base address: the explicit override when present, otherwise the
    /// specification server template with <see cref="Subdomain"/> substituted for <c>{site}</c>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim();
            if (!Uri.TryCreate(EnsureTrailingSlash(trimmed), UriKind.Absolute, out var overridden))
            {
                throw new InvalidOperationException($"Maxio:BaseUrl is not a valid absolute URL: '{BaseUrl}'.");
            }

            return overridden;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/");
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
