using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration section.
/// Values are supplied through user-secrets / environment configuration and are never committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic <c>username</c> with the literal password <c>x</c>, per the
    /// <c>BasicAuth</c> security scheme in the Maxio OpenAPI specification.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The Advanced Billing site subdomain. Substituted into the <c>site</c> server variable of the
    /// specification's server template (<c>https://{site}.chargify.com</c>).
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional absolute base address override. When set it is used verbatim instead of deriving the address
    /// from <see cref="Subdomain"/> — required for sites that are not on the default US server template
    /// (e.g. the EU environment).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional override for the payment collection method used when creating subscriptions. When left unset
    /// the collection method is resolved from the site's invoicing architecture, so that plans which do not
    /// require a stored payment method can be subscribed to without capturing a card.
    /// Allowed values come from the specification's <c>Collection Method</c> enum.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Per-request timeout for calls to Maxio.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a transient failure (429 / 5xx / network fault) is retried.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential retry backoff.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long the (slow moving) plan catalog is cached for.</summary>
    public TimeSpan CatalogCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The server template declared by the Maxio OpenAPI specification.</summary>
    internal const string ServerUrlTemplate = "https://{site}.chargify.com";

    /// <summary>
    /// Resolves the API base address: the verbatim <see cref="BaseUrl"/> when supplied, otherwise the
    /// specification's server template with the <c>site</c> variable replaced by <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl!.Trim();

            // HttpClient only appends relative request URIs when the base address ends in a slash.
            return new Uri(trimmed.EndsWith("/", StringComparison.Ordinal) ? trimmed : trimmed + "/", UriKind.Absolute);
        }

        return new Uri(ServerUrlTemplate.Replace("{site}", Subdomain!.Trim()) + "/", UriKind.Absolute);
    }

    /// <summary>Returns the configuration problems that prevent the integration from being used.</summary>
    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            failures.Add($"'{SectionName}:{nameof(ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            failures.Add($"'{SectionName}:{nameof(Subdomain)}' is required unless '{SectionName}:{nameof(BaseUrl)}' is set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !Uri.TryCreate(BaseUrl!.Trim(), UriKind.Absolute, out _))
        {
            failures.Add($"'{SectionName}:{nameof(BaseUrl)}' must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            failures.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is required.");
        }

        if (MaxRetryAttempts < 0)
        {
            failures.Add($"'{SectionName}:{nameof(MaxRetryAttempts)}' cannot be negative.");
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            failures.Add($"'{SectionName}:{nameof(RequestTimeout)}' must be greater than zero.");
        }

        return failures;
    }
}
