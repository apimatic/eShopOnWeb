using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" section.
/// Values are supplied by configuration/user-secrets/environment - never hard coded - so the same
/// build can target a different Maxio site and a different catalog.
/// </summary>
public class MaxioSettings
{
    public const string ConfigurationSection = "Maxio";

    /// <summary>Maxio API key. Sent as the HTTP Basic user name (password is the literal "x").</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Advanced Billing site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Optional absolute base URL override. When set it is used verbatim as the API base address
    /// instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Handle of the product family that contains the subscription plans offered by this storefront.
    /// When empty, plans are listed site-wide.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Maxio hosting environment used to derive the base URL, per the specification's
    /// x-server-configuration block: "US" (default) or "EU". Ignored when <see cref="BaseUrl"/> is set.
    /// </summary>
    public string Environment { get; set; } = MaxioEnvironments.Us;

    /// <summary>
    /// Optional plan handle used when a subscribe request does not name one.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Retries attempted for transient failures (timeouts, 429, 5xx) on top of the first try.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>
    /// Optional override for the payment collection method sent on new subscriptions
    /// (maxio-spec components/schemas/Collection-Method.yaml: automatic, remittance, prepaid, invoice).
    /// When empty, the integration picks one based on the plan and the site's invoicing architecture.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Seconds a listed plan catalog is cached for. Zero disables caching.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>Per-request timeout, clamped to a sane range.</summary>
    public TimeSpan ResolveTimeout() => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 300));

    public int ResolveRetryAttempts() => Math.Clamp(MaxRetryAttempts, 0, 10);

    public TimeSpan ResolveRetryBaseDelay() => TimeSpan.FromMilliseconds(Math.Clamp(RetryBaseDelayMilliseconds, 10, 60_000));

    /// <summary>Plan catalog cache duration, or null when caching is disabled.</summary>
    public TimeSpan? ResolvePlanCacheDuration() =>
        PlanCacheSeconds <= 0 ? null : TimeSpan.FromSeconds(Math.Min(PlanCacheSeconds, 3600));

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey)
        && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>
    /// Resolves the API base address. Servers are templated by the specification as
    /// https://{site}.chargify.com (US) and https://{site}.ebilling.maxio.com (EU).
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var configured = BaseUrl!.Trim();
            if (!Uri.TryCreate(EnsureTrailingSlash(configured), UriKind.Absolute, out var absolute))
            {
                throw new InvalidOperationException($"Maxio:BaseUrl '{BaseUrl}' is not a valid absolute URL.");
            }

            return absolute;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Either Maxio:Subdomain or Maxio:BaseUrl must be configured.");
        }

        var site = Subdomain!.Trim();
        var host = MaxioEnvironments.IsEu(Environment)
            ? $"https://{site}.ebilling.maxio.com/"
            : $"https://{site}.chargify.com/";

        return new Uri(host, UriKind.Absolute);
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}

public static class MaxioEnvironments
{
    public const string Us = "US";
    public const string Eu = "EU";

    public static bool IsEu(string? environment) =>
        string.Equals(environment?.Trim(), Eu, StringComparison.OrdinalIgnoreCase);
}
