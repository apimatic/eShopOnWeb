using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Secrets (the API key) are expected to come from user-secrets, environment
/// variables or a vault - never from a file in the repository.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the HTTP Basic user name (the password is always "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain, used to template the server URL from the spec.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional absolute base address override. When set it is used verbatim and the
    /// subdomain / environment templating is skipped.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Hosting environment of the Advanced Billing site, per the spec's server configuration:
    /// "US" (https://{site}.chargify.com) or "EU" (https://{site}.ebilling.maxio.com).
    /// </summary>
    public string Environment { get; set; } = MaxioEnvironments.Us;

    /// <summary>
    /// Handle of the plan used when a subscribe request does not name one. Optional: when it
    /// is not configured, callers must supply a plan handle.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Payment collection method used for new subscriptions. "remittance" (invoice billing)
    /// lets a shopper subscribe without a stored payment method; "automatic" requires one.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Prefix for the customer/subscription reference values written into Maxio.</summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>Per-request timeout, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a throttled or transient failure is retried before giving up.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential retry backoff, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>How long the plan catalog is cached in memory. Zero disables caching.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>Collection methods accepted by the Maxio spec (Collection-Method schema).</summary>
    private static readonly HashSet<string> AllowedCollectionMethods =
        new(StringComparer.OrdinalIgnoreCase) { "automatic", "remittance", "prepaid", "invoice" };

    /// <summary>
    /// Returns the configuration problems that make the integration unusable; empty when the
    /// options are usable.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"'{SectionName}:{nameof(ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            errors.Add($"'{SectionName}:{nameof(Subdomain)}' is required unless '{SectionName}:{nameof(BaseUrl)}' is set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add($"'{SectionName}:{nameof(BaseUrl)}' must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is required.");
        }

        if (!MaxioEnvironments.IsKnown(Environment))
        {
            errors.Add($"'{SectionName}:{nameof(Environment)}' must be one of: {string.Join(", ", MaxioEnvironments.All)}.");
        }

        if (!AllowedCollectionMethods.Contains(PaymentCollectionMethod))
        {
            errors.Add($"'{SectionName}:{nameof(PaymentCollectionMethod)}' must be one of: {string.Join(", ", AllowedCollectionMethods)}.");
        }

        if (TimeoutSeconds <= 0)
        {
            errors.Add($"'{SectionName}:{nameof(TimeoutSeconds)}' must be greater than zero.");
        }

        if (MaxRetryAttempts < 0)
        {
            errors.Add($"'{SectionName}:{nameof(MaxRetryAttempts)}' cannot be negative.");
        }

        return errors;
    }

    /// <summary>
    /// Resolves the API base address: the explicit override when present, otherwise the
    /// environment's server URL from the spec with the site subdomain substituted in.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(EnsureTrailingSlash(BaseUrl!), UriKind.Absolute);
        }

        var template = MaxioEnvironments.ServerUrlTemplate(Environment);
        return new Uri(EnsureTrailingSlash(template.Replace("{site}", Subdomain, StringComparison.Ordinal)), UriKind.Absolute);
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
