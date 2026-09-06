using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Binds the <c>Maxio</c> configuration section. Nothing here has a hard-coded site or catalog
/// value: the same build runs against any Maxio site by changing configuration alone.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key. Used as the user name of the HTTP Basic credential, with
    /// the literal password <c>x</c>, per Maxio's authentication guide. Secret — supply via
    /// user-secrets, environment or a vault, never via a file in source control.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Subdomain of the Maxio site, e.g. the <c>acme</c> in <c>acme.chargify.com</c>.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim (the only
    /// normalization is ensuring a single trailing slash so relative paths compose); when unset
    /// the address is derived from <see cref="Subdomain"/>. Set this for Maxio EU hosting, a
    /// vanity domain, or a record/replay proxy in tests.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional plan handle used when a subscribe request does not name one. Unset by default so
    /// that no catalog value is baked into the build.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Payment collection method requested for new subscriptions. Defaults to <c>remittance</c>,
    /// which invoices the subscriber and therefore succeeds on plans that do not require a payment
    /// method on file. Set to <c>automatic</c> on sites that capture a card before enrollment.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>
    /// Per-request timeout. Maxio cuts every request off at 120 seconds, so a client timeout at or
    /// below that keeps a wedged call from pinning a request thread indefinitely.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 100;

    /// <summary>Total attempts for a retryable call, including the first. 1 disables retries.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries, in milliseconds.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// Ceiling on in-flight requests to the Maxio site. Maxio limits a subdomain to four concurrent
    /// API calls and queues the excess, so holding the line here is cheaper than being throttled.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>How long the plan catalog and site currency are cached, in seconds. 0 disables caching.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>True when the minimum settings needed to talk to Maxio are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>
    /// Resolves the API base address: <see cref="BaseUrl"/> verbatim when set, otherwise derived
    /// from <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain.Trim()}.chargify.com"
            : BaseUrl.Trim();

        return new Uri(value.TrimEnd('/') + "/", UriKind.Absolute);
    }

    /// <summary>Returns a message per invalid setting; empty when the settings are usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            errors.Add($"'{SectionName}:ApiKey' is required.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            errors.Add($"'{SectionName}:Subdomain' is required unless '{SectionName}:BaseUrl' is set.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            errors.Add($"'{SectionName}:ProductFamilyHandle' is required.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out _))
        {
            errors.Add($"'{SectionName}:BaseUrl' must be an absolute URI when set.");
        }

        if (string.IsNullOrWhiteSpace(PaymentCollectionMethod))
        {
            errors.Add($"'{SectionName}:PaymentCollectionMethod' must not be blank.");
        }

        if (TimeoutSeconds is < 1 or > 120)
        {
            errors.Add($"'{SectionName}:TimeoutSeconds' must be between 1 and 120.");
        }

        if (MaxAttempts is < 1 or > 10)
        {
            errors.Add($"'{SectionName}:MaxAttempts' must be between 1 and 10.");
        }

        if (RetryBaseDelayMilliseconds is < 0 or > 30_000)
        {
            errors.Add($"'{SectionName}:RetryBaseDelayMilliseconds' must be between 0 and 30000.");
        }

        if (MaxConcurrentRequests is < 1 or > 16)
        {
            errors.Add($"'{SectionName}:MaxConcurrentRequests' must be between 1 and 16.");
        }

        if (CatalogCacheSeconds is < 0 or > 3600)
        {
            errors.Add($"'{SectionName}:CatalogCacheSeconds' must be between 0 and 3600.");
        }

        return errors;
    }
}
