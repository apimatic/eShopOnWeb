using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. No value has a default that points at a real site or
/// catalog: every deployment supplies its own (user-secrets in development, environment variables
/// <c>Maxio__ApiKey</c> etc. or a secret store elsewhere).
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site's API key (HTTP Basic username). Secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Maxio:Subdomain</c> — the site subdomain; used to derive the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary><c>Maxio:Environment</c> — optional; <c>US</c> (default) or <c>EU</c> data-centre.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// <c>Maxio:RequestBudgetSeconds</c> — optional; total time one inbound API request may spend on billing
    /// calls (all calls and retries together). Default 30.
    /// </summary>
    public int RequestBudgetSeconds { get; set; } = 30;

    /// <summary><c>Maxio:PlanCacheSeconds</c> — optional; how long the plan list is cached. Default 60; 0 disables.</summary>
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> — optional; how new subscriptions are billed. This flow captures no
    /// payment method, so the default is <c>remittance</c> (invoice the customer; Relationship Invoicing sites).
    /// Legacy Statements sites use <c>invoice</c>. <c>automatic</c> requires a payment method on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    internal static readonly string[] KnownCollectionMethods = { "remittance", "invoice", "automatic", "prepaid" };

    public bool IsEuEnvironment => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Refuses to start the host when a required Maxio setting is missing or malformed.</summary>
public sealed class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        // Messages name the configuration key only — never the value.
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, the Maxio__ApiKey environment variable or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                failures.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
            }
            else if (Uri.CheckHostName(settings.Subdomain.Trim()) == UriHostNameType.Unknown || settings.Subdomain.Contains('.'))
            {
                failures.Add("Maxio:Subdomain must be a bare site subdomain (no scheme, dots or path).");
            }
        }
        else if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL when set.");
        }

        if (!string.IsNullOrWhiteSpace(settings.Environment)
            && !string.Equals(settings.Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.Environment.Trim(), "EU", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Maxio:Environment must be 'US' or 'EU' when set.");
        }

        if (settings.RequestBudgetSeconds is < 5 or > 120)
        {
            failures.Add("Maxio:RequestBudgetSeconds must be between 5 and 120.");
        }

        if (Array.IndexOf(MaxioSettings.KnownCollectionMethods, settings.PaymentCollectionMethod?.Trim().ToLowerInvariant()) < 0)
        {
            failures.Add("Maxio:PaymentCollectionMethod must be one of: remittance, invoice, automatic, prepaid.");
        }

        if (settings.PlanCacheSeconds < 0)
        {
            failures.Add("Maxio:PlanCacheSeconds must not be negative.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
