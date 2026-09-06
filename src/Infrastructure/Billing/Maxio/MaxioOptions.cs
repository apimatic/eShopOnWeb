using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio:</c>
/// configuration section.
/// </summary>
/// <remarks>
/// None of these values are ever committed. Supply them through .NET user-secrets in
/// development (<c>dotnet user-secrets set "Maxio:ApiKey" ...</c>) or through environment
/// variables / a secret store in production (<c>Maxio__ApiKey</c>, ...).
/// </remarks>
public sealed class MaxioOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>Site API key, used as the HTTP Basic username with a literal "X" password.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, e.g. the "acme" in <c>https://acme.chargify.com</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional API base address override. When set it is used as given, instead of deriving
    /// <c>https://{Subdomain}.chargify.com</c>. Useful for pinning a non-default host or for
    /// pointing tests at a stub.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Host requests are sent to when <see cref="BaseUrl"/> is not supplied.</summary>
    public string ApiHostSuffix { get; set; } = "chargify.com";

    /// <summary>Timeout applied to each attempt. Maxio itself cuts requests off at 120s.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Retries attempted after a throttled, timed-out or 5xx response.</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// Cap on in-flight requests to Maxio. Maxio limits a subdomain to four concurrent API
    /// workers and queues the rest, so exceeding this only makes responses slower.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>How long the plan catalog and site metadata are cached for.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>
    /// How recurring charges are collected: "remittance" or "prepaid" on Relationship Invoicing
    /// sites, "invoice" on statement-based ones, or "automatic" on either.
    /// </summary>
    /// <remarks>
    /// Left unset, an invoice-style method is chosen to match the site's architecture. That is the
    /// only setting that can work here: "automatic" charges the card on file at signup, and this
    /// integration deliberately captures no payment details, so an automatic signup is rejected
    /// with "No payment method was on file". Deployments that do capture cards (via Billing.js and
    /// a payment profile) should set this to "automatic".
    /// </remarks>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// Namespace prefix for the customer <c>reference</c> written into Maxio, so eShopOnWeb
    /// customers stay distinguishable from anything else sharing the site.
    /// </summary>
    public string CustomerReferencePrefix { get; set; } = "eshoponweb";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle)
        && (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    /// <summary>
    /// Throws <see cref="BillingNotConfiguredException"/> listing every missing setting, so an
    /// operator sees the whole problem at once instead of one key per attempt.
    /// </summary>
    public void EnsureConfigured()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            missing.Add($"{SectionName}:{nameof(ApiKey)}");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            missing.Add($"{SectionName}:{nameof(ProductFamilyHandle)}");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            missing.Add($"{SectionName}:{nameof(Subdomain)} (or {SectionName}:{nameof(BaseUrl)})");
        }

        if (missing.Count > 0)
        {
            throw new BillingNotConfiguredException(
                "Maxio billing is not configured. Missing setting(s): " + string.Join(", ", missing) +
                ". Set them with user-secrets (dotnet user-secrets set \"Maxio:ApiKey\" ...) " +
                "or the matching Maxio__* environment variables.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new BillingNotConfiguredException($"{SectionName}:{nameof(TimeoutSeconds)} must be greater than zero.");
        }

        if (MaxConcurrentRequests <= 0)
        {
            throw new BillingNotConfiguredException($"{SectionName}:{nameof(MaxConcurrentRequests)} must be greater than zero.");
        }

        _ = ResolveBaseAddress();
    }

    /// <summary>
    /// The address requests are sent to: <see cref="BaseUrl"/> verbatim when supplied (a trailing
    /// slash is added if absent so relative paths append rather than replace the last segment),
    /// otherwise derived from <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        var raw = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain!.Trim()}.{ApiHostSuffix.Trim().TrimStart('.')}"
            : BaseUrl!.Trim();

        if (!raw.EndsWith("/", StringComparison.Ordinal))
        {
            raw += "/";
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            throw new BillingNotConfiguredException(
                $"{SectionName}:{nameof(BaseUrl)} is not a valid absolute URL: '{BaseUrl}'.");
        }

        return uri;
    }
}
