using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Options bound from the <c>Maxio</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="ApiKey"/> is a credential: supply it through user-secrets, environment variables
/// (<c>Maxio__ApiKey</c>) or a secret store. It must never be committed to <c>appsettings*.json</c>.
/// </remarks>
public sealed class MaxioSettings : IValidatableObject
{
    public const string SectionName = "Maxio";

    /// <summary>Advanced Billing API key. Sent as the HTTP Basic user name, with the literal password "x".</summary>
    public string? ApiKey { get; set; }

    /// <summary>Subdomain of the Advanced Billing site, e.g. <c>acme</c> for <c>https://acme.chargify.com</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim, instead of the address
    /// derived from <see cref="Subdomain"/> and <see cref="Environment"/>. Useful for pointing the
    /// integration at a gateway, a proxy, or a record/replay server.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Advanced Billing hosting region: <c>US</c> (default) or <c>EU</c>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Handle of the plan used when a subscribe request does not name one. Leave unset to require
    /// callers to always name a plan.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// How Advanced Billing collects payment for subscriptions this integration creates.
    /// <c>remittance</c> (invoice the customer) is the default because it does not require a payment
    /// method on file. Use <c>automatic</c> only on sites that capture cards before subscribing.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Prefix for the customer and subscription reference values this integration owns.</summary>
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>How long the plan catalog and site currency are cached. Zero disables caching.</summary>
    public TimeSpan CatalogCacheDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Per-request timeout for calls to Advanced Billing.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many times a failed read is retried. Writes are never retried at the transport layer.</summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Client-side ceiling on requests in flight to Advanced Billing at once. Advanced Billing limits
    /// concurrency per site, so queueing here produces cleaner backpressure than collecting 429s.
    /// </summary>
    [Range(1, 64)]
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>The base address to call, honouring <see cref="BaseUrl"/> when it is set.</summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var overridden = BaseUrl.Trim();

            // Relative segments only compose onto a base URI whose path ends in a slash.
            if (!overridden.EndsWith("/", StringComparison.Ordinal))
            {
                overridden += "/";
            }

            return new Uri(overridden, UriKind.Absolute);
        }

        var host = IsEuropeanEnvironment() ? "ebilling.maxio.com" : "chargify.com";
        return new Uri($"https://{Subdomain}.{host}/", UriKind.Absolute);
    }

    public bool IsEuropeanEnvironment() =>
        string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return Fail(
                $"{SectionName}:ApiKey is required. Set it with " +
                $"'dotnet user-secrets set {SectionName}:ApiKey <key>' or the {SectionName}__ApiKey environment variable.",
                nameof(ApiKey));
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            yield return Fail($"{SectionName}:ProductFamilyHandle is required.", nameof(ProductFamilyHandle));
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(BaseUrl);
        Uri? parsedBaseUrl = null;

        if (!hasBaseUrl && string.IsNullOrWhiteSpace(Subdomain))
        {
            yield return Fail(
                $"{SectionName}:Subdomain is required unless {SectionName}:BaseUrl is set.",
                nameof(Subdomain));
        }

        if (hasBaseUrl && !Uri.TryCreate(BaseUrl!.Trim(), UriKind.Absolute, out parsedBaseUrl))
        {
            yield return Fail($"{SectionName}:BaseUrl must be an absolute URL.", nameof(BaseUrl));
        }
        else if (parsedBaseUrl is not null &&
                 parsedBaseUrl.Scheme != Uri.UriSchemeHttps &&
                 parsedBaseUrl.Scheme != Uri.UriSchemeHttp)
        {
            yield return Fail($"{SectionName}:BaseUrl must use http or https.", nameof(BaseUrl));
        }

        if (!string.Equals(Environment, "US", StringComparison.OrdinalIgnoreCase) && !IsEuropeanEnvironment())
        {
            yield return Fail($"{SectionName}:Environment must be 'US' or 'EU'.", nameof(Environment));
        }

        if (!MaxioCollectionMethods.IsSupported(PaymentCollectionMethod))
        {
            yield return Fail(
                $"{SectionName}:PaymentCollectionMethod must be one of: {MaxioCollectionMethods.SupportedList}.",
                nameof(PaymentCollectionMethod));
        }

        if (string.IsNullOrWhiteSpace(ReferencePrefix))
        {
            yield return Fail($"{SectionName}:ReferencePrefix cannot be blank.", nameof(ReferencePrefix));
        }

        if (CatalogCacheDuration < TimeSpan.Zero)
        {
            yield return Fail($"{SectionName}:CatalogCacheDuration cannot be negative.", nameof(CatalogCacheDuration));
        }

        if (Timeout <= TimeSpan.Zero)
        {
            yield return Fail($"{SectionName}:Timeout must be positive.", nameof(Timeout));
        }

        if (RetryBaseDelay < TimeSpan.Zero)
        {
            yield return Fail($"{SectionName}:RetryBaseDelay cannot be negative.", nameof(RetryBaseDelay));
        }
    }

    private static ValidationResult Fail(string message, string member) => new(message, new[] { member });
}
