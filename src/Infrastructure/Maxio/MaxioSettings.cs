using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> section.
/// </summary>
/// <remarks>
/// Every value here is deployment-specific and must come from configuration — user-secrets in
/// development, the platform's secret store in production. Nothing in this class carries a default
/// that would tie the build to one Maxio site or one catalog.
/// </remarks>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Used as the user name of the HTTP Basic credential, with the literal password
    /// <c>x</c>, which is the scheme Maxio's API documents.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. the <c>acme</c> in <c>https://acme.chargify.com</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim; otherwise the base
    /// address is derived from <see cref="Subdomain"/>. Useful for pointing at a proxy, a record/replay
    /// fixture, or a non-default Maxio host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Collection method to request when creating subscriptions. Defaults to <c>remittance</c>
    /// (invoice the customer) because that is what lets a shopper enrol without a stored card; sites
    /// that capture payment details up front can set this to <c>automatic</c>.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Request timeout applied to the Maxio HTTP client.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Total attempts (initial try plus retries) for transient Maxio failures.</summary>
    public int MaxAttempts { get; set; } = 3;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when provided, otherwise
    /// <c>https://{Subdomain}.chargify.com/</c>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var explicitUrl = BaseUrl!.Trim();
            if (!Uri.TryCreate(EnsureTrailingSlash(explicitUrl), UriKind.Absolute, out var parsed))
            {
                throw new InvalidOperationException(
                    $"'{MaxioSettings.SectionName}:{nameof(BaseUrl)}' is not a valid absolute URL.");
            }

            return parsed;
        }

        return new Uri($"https://{Subdomain!.Trim()}.chargify.com/");
    }

    /// <summary>Describes what is missing, for diagnostics that must never echo secret values.</summary>
    public IReadOnlyList<string> DescribeMissingSettings()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            missing.Add($"{SectionName}:{nameof(ApiKey)}");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            missing.Add($"{SectionName}:{nameof(Subdomain)} (or {SectionName}:{nameof(BaseUrl)})");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            missing.Add($"{SectionName}:{nameof(ProductFamilyHandle)}");
        }

        return missing;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/";
}
