using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Nothing here has a hard-coded default that points at a particular Maxio site or catalog:
/// the same build has to run against any site.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the HTTP Basic username (password "X"), per the Billing API
    /// authentication docs. Supply through user-secrets or the environment - never in a config file.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim and
    /// <see cref="Subdomain"/> is not consulted for routing.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional plan handle used when a subscribe request does not name one. Left unset by default
    /// so the deployment - not the code - decides which plan is the default target.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>How long the plan catalog and site metadata are cached. Zero disables caching.</summary>
    public int CatalogCacheSeconds { get; set; } = 60;

    /// <summary>
    /// Optional override for how Maxio collects payment on subscriptions this app creates. Left unset
    /// by default, in which case the integration picks the site's non-automatic method ("remittance",
    /// or "invoice" on legacy Statements sites): eShopOnWeb captures no card at signup, so asking
    /// Maxio to charge one automatically would fail the first invoice. Set to "automatic" once a
    /// payment method is captured before subscribing.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Retries after the first attempt for throttled, transient or transport failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// Maxio throttles per site on concurrency rather than request rate, and documents a ceiling of
    /// four concurrent calls. Requests above this are queued locally instead of being throttled remotely.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 4;

    /// <summary>
    /// How many times to re-read after Maxio reports a duplicate submission, while the winning
    /// request finishes committing.
    /// </summary>
    public int DuplicateResolutionAttempts { get; set; } = 3;

    public int DuplicateResolutionDelayMilliseconds { get; set; } = 250;

    /// <summary>
    /// Size of the window over which a repeated subscribe collapses onto the same Maxio
    /// uniqueness token. Long enough to absorb double-clicks and client retries, short enough that a
    /// deliberate re-subscribe after a cancellation is not rejected as a duplicate.
    /// </summary>
    public int IdempotencyWindowSeconds { get; set; } = 300;

    /// <summary>
    /// Resolves the API base address: the explicit override when given, otherwise the US-hosted
    /// Billing API address for the configured site subdomain.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(EnsureTrailingSlash(BaseUrl.Trim()), UriKind.Absolute, out var explicitUri))
            {
                throw new BillingConfigurationException(
                    $"'{SectionName}:BaseUrl' is not a valid absolute URL.");
            }

            return explicitUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new BillingConfigurationException(
                $"'{SectionName}:Subdomain' is required when '{SectionName}:BaseUrl' is not set.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    /// <summary>
    /// Resolves the API base address without throwing, for callers that must not fail before the
    /// misconfiguration can be reported properly on the request that needs it.
    /// </summary>
    public bool TryResolveBaseAddress(out Uri? baseAddress)
    {
        try
        {
            baseAddress = ResolveBaseAddress();
            return true;
        }
        catch (BillingConfigurationException)
        {
            baseAddress = null;
            return false;
        }
    }

    /// <summary>Throws <see cref="BillingConfigurationException"/> if the integration cannot run.</summary>
    public void Validate()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            missing.Add($"{SectionName}:ApiKey");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            missing.Add($"{SectionName}:Subdomain");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            missing.Add($"{SectionName}:ProductFamilyHandle");
        }

        if (missing.Count > 0)
        {
            throw new BillingConfigurationException(
                "Maxio billing is not configured. Missing configuration: " + string.Join(", ", missing) +
                ". Set these via user-secrets or environment variables.");
        }

        // Surfaces a malformed BaseUrl at startup rather than on the first request.
        ResolveBaseAddress();
    }

    /// <summary>True when enough is configured for the integration to be enabled at all.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
