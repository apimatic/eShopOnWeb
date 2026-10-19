using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section. Values come from configuration only
/// (user-secrets in development, environment variables / a secret store elsewhere) — never from source.
/// </summary>
public class MaxioSettings : IValidatableObject
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio Advanced Billing API key (sent as the Basic-auth user name).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Maxio site subdomain, substituted into the API base address.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional override: when set, used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Hosting region: <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>
    /// How Maxio collects payment for new subscriptions: <c>remittance</c> (default: invoiced, paid out-of-band;
    /// eShopOnWeb captures no card), <c>automatic</c> (charges a payment method on file), <c>prepaid</c> or <c>invoice</c> (legacy sites).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";

    /// <summary>Per-attempt timeout for one HTTP exchange with Maxio.</summary>
    [Range(1, 120)]
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>Upper bound for one billing operation, retries and follow-up reads included.</summary>
    [Range(1, 300)]
    public int RequestBudgetSeconds { get; set; } = 30;

    /// <summary>How long the plan list is cached.</summary>
    [Range(0, 3600)]
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>How long an in-flight enrollment claim is honoured before another request may take it over.</summary>
    [Range(30, 3600)]
    public int PendingClaimTimeoutSeconds { get; set; } = 120;

    public CollectionMethod CollectionMethod =>
        CollectionMethod.TryGetKnownValue(PaymentCollectionMethod.Trim(), out var known) ? known : CollectionMethod.Remittance;

    public bool IsEu => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Messages name the configuration key only — never the value.
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return new ValidationResult($"{CONFIG_NAME}:ApiKey is not configured. Set it via user-secrets, an environment variable or your secret store.", new[] { nameof(ApiKey) });
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            yield return new ValidationResult($"{CONFIG_NAME}:ProductFamilyHandle is not configured.", new[] { nameof(ProductFamilyHandle) });
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                yield return new ValidationResult($"{CONFIG_NAME}:Subdomain is not configured (required unless {CONFIG_NAME}:BaseUrl is set).", new[] { nameof(Subdomain) });
            }
        }
        else if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            yield return new ValidationResult($"{CONFIG_NAME}:BaseUrl must be an absolute http(s) URL.", new[] { nameof(BaseUrl) });
        }

        if (!CollectionMethod.TryGetKnownValue(PaymentCollectionMethod?.Trim() ?? string.Empty, out _))
        {
            yield return new ValidationResult($"{CONFIG_NAME}:PaymentCollectionMethod must be one of: remittance, automatic, prepaid, invoice.", new[] { nameof(PaymentCollectionMethod) });
        }

        if (!string.IsNullOrWhiteSpace(Environment)
            && !string.Equals(Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)
            && !IsEu)
        {
            yield return new ValidationResult($"{CONFIG_NAME}:Environment must be 'US' or 'EU'.", new[] { nameof(Environment) });
        }
    }
}
