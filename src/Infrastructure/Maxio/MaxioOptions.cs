using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// </summary>
/// <remarks>
/// Nothing here has a hard-coded default that ties the build to one Maxio site or catalog: the
/// same binary must run against a different site and a different product family purely by configuration.
/// Values are supplied by user-secrets in development and by environment variables elsewhere.
/// </remarks>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic username, with the literal password "x",
    /// per the spec's <c>BasicAuth</c> security scheme.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain, used to fill the spec's <c>{site}</c> server template.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional absolute base address override. When set it is used verbatim and
    /// <see cref="Subdomain"/>/<see cref="Environment"/> are not used to derive one.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Hosting environment of the Advanced Billing site: "US" (default) or "EU".
    /// Selects which server template from the spec's <c>x-server-configuration</c> is used.
    /// </summary>
    public string Environment { get; set; } = DefaultEnvironment;

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Number of retries attempted on top of the initial attempt for retry-safe failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for exponential backoff between retries, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>How long the site's default currency is cached for. It effectively never changes.</summary>
    public int SiteCacheMinutes { get; set; } = 30;

    public const string DefaultEnvironment = "US";

    /// <summary>
    /// The <c>production</c> server templates declared by the Maxio OpenAPI spec
    /// (<c>info.x-server-configuration.environments[].servers[]</c>), keyed by environment name.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ServerTemplates =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["US"] = "https://{site}.chargify.com",
            ["EU"] = "https://{site}.ebilling.maxio.com"
        };

    public static bool IsKnownEnvironment(string? environment) =>
        !string.IsNullOrWhiteSpace(environment) && ServerTemplates.ContainsKey(environment);

    /// <summary>
    /// Resolves the API base address: the <see cref="BaseUrl"/> override when supplied, otherwise the
    /// spec's server template for <see cref="Environment"/> with <c>{site}</c> replaced by <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(EnsureTrailingSlash(BaseUrl.Trim()), UriKind.Absolute);
        }

        var environment = string.IsNullOrWhiteSpace(Environment) ? DefaultEnvironment : Environment.Trim();
        if (!ServerTemplates.TryGetValue(environment, out var template))
        {
            throw new InvalidOperationException(
                $"Unsupported Maxio environment '{environment}'. The OpenAPI specification declares only: {string.Join(", ", ServerTemplates.Keys)}.");
        }

        var subdomain = Subdomain?.Trim();
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Subdomain is required unless {SectionName}:BaseUrl is set.");
        }

        return new Uri(EnsureTrailingSlash(template.Replace("{site}", subdomain, StringComparison.Ordinal)), UriKind.Absolute);
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
