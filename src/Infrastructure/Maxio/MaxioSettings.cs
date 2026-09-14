using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> section.
/// </summary>
/// <remarks>
/// Every value is deployment specific — nothing about a particular Maxio site or catalog is compiled
/// in. <see cref="ApiKey"/> is a secret and must come from user-secrets, environment variables or a
/// vault; it is never stored in the repository.
/// </remarks>
public class MaxioSettings
{
    /// <summary>Name of the configuration section these settings are bound from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key, used as the username of the HTTP Basic credential (password "X").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Maxio site to talk to, e.g. the site's <c>{site}.chargify.com</c> prefix.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional absolute API base address. When set it is used verbatim and <see cref="Subdomain"/>
    /// and <see cref="Environment"/> are not used to derive one.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio hosting environment, <c>US</c> (default) or <c>EU</c>. Selects the host template used
    /// when <see cref="BaseUrl"/> is not supplied.
    /// </summary>
    public string Environment { get; set; } = MaxioEnvironments.US;

    /// <summary>
    /// Optional plan handle used when a subscribe request does not name one. When unset, callers must
    /// name the plan explicitly.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How renewals are collected for subscriptions this application creates. <c>remittance</c> bills
    /// by invoice and therefore needs no stored card at signup; <c>automatic</c> charges a stored
    /// payment method and will fail for shoppers who have none.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>How long the plan catalog is cached for. Zero disables caching.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>Per-request timeout, in seconds. Maxio cuts requests off at 120 seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How many times a request is retried after a throttling or transient failure. Maxio limits a
    /// site to four concurrent calls, so retries back off rather than fan out.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// How long two subscribe attempts for the same shopper and plan are treated as the same logical
    /// request. Kept short: it only has to span a double-click or a replayed HTTP request, and Maxio
    /// consumes the uniqueness token even for a rejected attempt.
    /// </summary>
    public int IdempotencyWindowSeconds { get; set; } = 120;

    /// <summary>Prefix applied to every customer and subscription reference this application creates.</summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>True when the mandatory settings are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when supplied, otherwise the
    /// host for <see cref="Environment"/> with <see cref="Subdomain"/> substituted in.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var baseUrl = BaseUrl!.Trim();

            // HttpClient drops the last path segment of a base address that does not end in a slash.
            if (!baseUrl.EndsWith('/'))
            {
                baseUrl += "/";
            }

            return new Uri(baseUrl, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"{SectionName}:Subdomain is required when {SectionName}:BaseUrl is not set.");
        }

        var host = MaxioEnvironments.HostTemplateFor(Environment).Replace("{site}", Subdomain!.Trim());
        return new Uri($"https://{host}/", UriKind.Absolute);
    }
}

/// <summary>
/// The Maxio hosting environments and the API host each one serves.
/// </summary>
public static class MaxioEnvironments
{
    public const string US = "US";
    public const string EU = "EU";

    public static string HostTemplateFor(string? environment) =>
        string.Equals(environment?.Trim(), EU, StringComparison.OrdinalIgnoreCase)
            ? "{site}.ebilling.maxio.com"
            : "{site}.chargify.com";
}
