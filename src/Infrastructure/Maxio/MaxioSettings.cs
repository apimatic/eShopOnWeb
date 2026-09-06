using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" section.
/// </summary>
/// <remarks>
/// Nothing in here is ever committed. On a developer machine these come from .NET user-secrets;
/// in a deployment they come from the environment or a secret store.
/// </remarks>
public class MaxioSettings
{
    public const string ConfigurationSectionName = "Maxio";

    /// <summary>
    /// Advanced Billing API key. Sent as the HTTP Basic username, with the literal "x" as the
    /// password, which is the scheme Maxio documents for the Advanced Billing API.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Advanced Billing site, used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim (a trailing slash
    /// is appended if absent so that relative request paths resolve underneath it) instead of
    /// being derived from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Hosting region of the Advanced Billing site: "US" (default) or "EU". Only consulted when
    /// <see cref="BaseUrl"/> is not supplied.
    /// </summary>
    public string Environment { get; set; } = UsEnvironment;

    /// <summary>
    /// How Maxio should collect payment for subscriptions this application creates.
    /// Defaults to "remittance" (invoice the customer) because the plans in this integration do
    /// not require a stored payment method, and "automatic" collection fails at signup when no
    /// card is on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Overall latency budget for a single API call, in seconds. It spans every retry, so a slow
    /// provider can never stall a request past this bound.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a transient failure is retried before giving up.</summary>
    public int MaxRetries { get; set; } = 3;

    private const string UsEnvironment = "US";
    private const string EuEnvironment = "EU";

    // Base addresses published by Maxio for the Advanced Billing API.
    private const string UsBaseUrlTemplate = "https://{0}.chargify.com";
    private const string EuBaseUrlTemplate = "https://{0}.ebilling.maxio.com";

    /// <summary>
    /// Names of the configuration keys that are missing or empty. Empty when fully configured.
    /// Returns key names only - never values.
    /// </summary>
    public IReadOnlyList<string> GetMissingSettings()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            missing.Add($"{ConfigurationSectionName}:{nameof(ApiKey)}");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            missing.Add($"{ConfigurationSectionName}:{nameof(Subdomain)} (or {ConfigurationSectionName}:{nameof(BaseUrl)})");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            missing.Add($"{ConfigurationSectionName}:{nameof(ProductFamilyHandle)}");
        }

        return missing;
    }

    public bool IsConfigured => GetMissingSettings().Count == 0;

    /// <summary>
    /// Resolves the API base address, or returns null when there is not enough configuration to
    /// build one.
    /// </summary>
    public Uri? ResolveBaseAddress()
    {
        var raw = BaseUrl;

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                return null;
            }

            var template = string.Equals(Environment, EuEnvironment, StringComparison.OrdinalIgnoreCase)
                ? EuBaseUrlTemplate
                : UsBaseUrlTemplate;

            raw = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Subdomain!.Trim());
        }

        raw = raw!.Trim();

        // HttpClient resolves relative request URIs against the base address, and that only keeps
        // the base path when it ends in a slash.
        if (!raw.EndsWith("/", StringComparison.Ordinal))
        {
            raw += "/";
        }

        return Uri.TryCreate(raw, UriKind.Absolute, out var uri) ? uri : null;
    }
}
