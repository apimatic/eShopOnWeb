using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Bound from the <c>Maxio</c> configuration section. Secrets (ApiKey) come from user-secrets in
/// development and from the environment / a secret store elsewhere — never from appsettings.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio Advanced Billing API key (sent as the Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, substituted into the environment's base URL. Not needed when <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional: used verbatim as the API base address instead of deriving one from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Hosting region: <c>US</c> (default) or <c>EU</c>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// How new subscriptions are collected: <c>remittance</c> (default — invoiced, no card on file needed;
    /// eShop does not capture cards), <c>automatic</c>, <c>prepaid</c>, or legacy <c>invoice</c>.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Bound on one HTTP attempt (the SDK retries GETs up to 3 times).</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>Bound on one whole SDK call, retries and backoff included.</summary>
    public int CallBudgetSeconds { get; set; } = 20;

    public bool IsEu => string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Refuses to start the host with missing or malformed Maxio settings. Never echoes a value.</summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();
        var prefix = MaxioSettings.SectionName;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{prefix}:ApiKey is not configured. Set it via user-secrets, the environment (Maxio__ApiKey) or your secret store.");
        }
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add($"{prefix}:ProductFamilyHandle is not configured.");
        }
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
            {
                failures.Add($"{prefix}:Subdomain is not configured (required unless {prefix}:BaseUrl is set).");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{prefix}:BaseUrl must be an absolute http(s) URL.");
        }
        if (!string.Equals(options.Environment, "US", StringComparison.OrdinalIgnoreCase) && !options.IsEu)
        {
            failures.Add($"{prefix}:Environment must be 'US' or 'EU'.");
        }
        if (!MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(options.PaymentCollectionMethod, out _))
        {
            failures.Add($"{prefix}:PaymentCollectionMethod must be one of: remittance, automatic, prepaid, invoice.");
        }
        if (options.AttemptTimeoutSeconds <= 0)
        {
            failures.Add($"{prefix}:AttemptTimeoutSeconds must be positive.");
        }
        if (options.CallBudgetSeconds < options.AttemptTimeoutSeconds)
        {
            failures.Add($"{prefix}:CallBudgetSeconds must be at least {prefix}:AttemptTimeoutSeconds.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
