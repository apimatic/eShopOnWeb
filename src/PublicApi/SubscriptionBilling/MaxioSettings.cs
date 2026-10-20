using System;
using System.Collections.Generic;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// Bound from the <c>Maxio:</c> configuration section. Values come from user-secrets in development
/// and from environment variables (<c>Maxio__ApiKey</c>, ...) or a secret store elsewhere - never from
/// a file in the repository.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key; sent as the Basic-auth user name.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain; substituted into the US production base URL.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set, used verbatim as the API base address instead of the subdomain-derived one.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional. How new subscriptions are collected. No card is captured by this integration, so the
    /// default is invoice-based <c>remittance</c> (Relationship Invoicing sites); legacy Statements sites
    /// use <c>invoice</c>. <c>automatic</c> only works once a payment method is on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Per-attempt timeout for a single HTTP attempt to Maxio.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Total budget for all Maxio calls made while serving one API request.</summary>
    public TimeSpan RequestBudget { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the subscription plan list is cached.</summary>
    public TimeSpan PlanCacheDuration { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Refuses to start the host when a required Maxio setting is missing, instead of failing with a 401
/// on the first call. Messages name the key, never the value.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, an environment variable or your secret store.");

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
            failures.Add("Maxio:ProductFamilyHandle is not configured.");

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
                failures.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when set.");
        }

        if (!CollectionMethod.TryGetKnownValue(options.PaymentCollectionMethod ?? string.Empty, out _))
            failures.Add("Maxio:PaymentCollectionMethod must be one of remittance, invoice, automatic, prepaid.");

        if (options.AttemptTimeout <= TimeSpan.Zero)
            failures.Add("Maxio:AttemptTimeout must be positive.");

        if (options.RequestBudget <= TimeSpan.Zero)
            failures.Add("Maxio:RequestBudget must be positive.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
