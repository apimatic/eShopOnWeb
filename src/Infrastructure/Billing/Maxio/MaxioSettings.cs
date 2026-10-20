using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Secrets come from user-secrets locally and from
/// the deployment's configuration elsewhere — never from a file in the repository.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the Basic-auth username. Required.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site subdomain, substituted into the provider's base URL. Required.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are the offered plans. Required.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim instead of deriving one from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> — how new subscriptions are collected. This flow captures no card,
    /// so the default is <c>remittance</c> (invoice-billed); <c>automatic</c> needs a payment method on file.
    /// Use <c>invoice</c> on a site still on the legacy statements architecture.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Whole-request budget for every provider call made while serving one API request.</summary>
    public TimeSpan RequestBudget { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound for one HTTP attempt.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long the plan list is cached.</summary>
    public TimeSpan PlanCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A claim still pending after this long belongs to an interrupted request and is re-checked against the provider.</summary>
    public TimeSpan PendingClaimStaleAfter { get; set; } = TimeSpan.FromMinutes(2);
}

public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        // Name the missing key, never echo a value.
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured.");
        }
        if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            failures.Add("Maxio:Subdomain is not configured (and no Maxio:BaseUrl override is set).");
        }
        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl)
            && (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when set.");
        }
        if (!string.IsNullOrWhiteSpace(settings.Environment)
            && !MaxioClientFactory.TryParseEnvironment(settings.Environment, out _))
        {
            failures.Add("Maxio:Environment must be US or EU when set.");
        }
        if (!MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(settings.PaymentCollectionMethod?.Trim(), out _))
        {
            failures.Add("Maxio:PaymentCollectionMethod must be one of automatic, remittance, prepaid, invoice.");
        }
        if (settings.RequestBudget <= TimeSpan.Zero || settings.AttemptTimeout <= TimeSpan.Zero)
        {
            failures.Add("Maxio:RequestBudget and Maxio:AttemptTimeout must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
