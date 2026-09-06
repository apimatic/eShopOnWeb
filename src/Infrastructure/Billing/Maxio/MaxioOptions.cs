using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Nothing here has a baked-in default that points at a particular site or catalog: the same
/// build runs against any Maxio site by changing configuration only.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the basic-auth user name with the fixed password <c>x</c>, per the
    /// <c>BasicAuth</c> security scheme in the Maxio OpenAPI specification.
    /// Supply through user-secrets or the environment - never through a file in this repository.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Subdomain of the Maxio site. Fills the <c>site</c> server variable of the specification's
    /// <c>https://{site}.chargify.com</c> server template.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// API handle of the product family whose products are offered as subscription plans.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional absolute base address override. When set it is used verbatim instead of deriving an
    /// address from <see cref="Subdomain"/> - for example an EU-hosted site
    /// (<c>https://{site}.ebilling.maxio.com</c>) or a local recording proxy.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional override for the payment collection method used when creating subscriptions. When
    /// empty, the method is derived from the site's billing architecture (see
    /// <see cref="MaxioSubscriptionBillingService"/>). Valid values come from the specification's
    /// <c>Collection Method</c> schema.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Per-request timeout applied to calls made to Maxio.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a transient Maxio failure (429 / 5xx / network error) is retried.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Valid <c>payment_collection_method</c> values from the Maxio "Collection Method" schema.</summary>
    public static readonly IReadOnlyList<string> ValidCollectionMethods =
        new[] { "automatic", "remittance", "prepaid", "invoice" };

    /// <summary>
    /// Base address for the Maxio API: the verbatim <see cref="BaseUrl"/> when supplied, otherwise the
    /// specification's production server template resolved with <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = !string.IsNullOrWhiteSpace(BaseUrl)
            ? BaseUrl!.Trim()
            : $"https://{Subdomain?.Trim()}.chargify.com";

        return new Uri(raw.EndsWith('/') ? raw : raw + "/", UriKind.Absolute);
    }

    /// <summary>Returns one message per configuration problem; an empty result means the options are usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            failures.Add($"'{SectionName}:{nameof(ApiKey)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            failures.Add($"'{SectionName}:{nameof(ProductFamilyHandle)}' is required.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            failures.Add($"'{SectionName}:{nameof(Subdomain)}' is required unless '{SectionName}:{nameof(BaseUrl)}' is set.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !IsHttpUri(BaseUrl))
        {
            failures.Add($"'{SectionName}:{nameof(BaseUrl)}' must be an absolute http or https URL.");
        }

        if (!string.IsNullOrWhiteSpace(PaymentCollectionMethod) &&
            !ValidCollectionMethods.Contains(PaymentCollectionMethod.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            failures.Add(
                $"'{SectionName}:{nameof(PaymentCollectionMethod)}' must be one of: {string.Join(", ", ValidCollectionMethods)}.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            failures.Add($"'{SectionName}:{nameof(Timeout)}' must be greater than zero.");
        }

        if (MaxRetries < 0)
        {
            failures.Add($"'{SectionName}:{nameof(MaxRetries)}' cannot be negative.");
        }

        return failures;
    }

    private static bool IsHttpUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
