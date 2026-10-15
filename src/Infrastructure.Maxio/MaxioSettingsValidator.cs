using System;
using System.Collections.Generic;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Refuses to start the host with incomplete Maxio settings. Messages name the configuration key,
/// never its value.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            failures.Add("Maxio:ApiKey is not configured. Set it via user-secrets, the Maxio__ApiKey or MAXIO_API_KEY environment variable, or your secret store.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is not configured (or MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                failures.Add("Maxio:Subdomain is not configured (or MAXIO_SITE_SUBDOMAIN); it is required unless Maxio:BaseUrl is set.");
            }
        }
        else if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL.");
        }

        if (!ServerEnvironment.TryGetKnownValue(settings.Environment?.Trim().ToUpperInvariant(), out _))
        {
            failures.Add("Maxio:Environment must be 'US' or 'EU'.");
        }

        if (!CollectionMethod.TryGetKnownValue(settings.PaymentCollectionMethod?.Trim().ToLowerInvariant(), out _))
        {
            failures.Add("Maxio:PaymentCollectionMethod must be one of 'remittance', 'invoice', 'automatic' or 'prepaid'.");
        }

        if (settings.AttemptTimeoutSeconds <= 0 || settings.OperationBudgetSeconds <= 0 || settings.SettlementBudgetSeconds <= 0
            || settings.PlanCacheSeconds < 0 || settings.MaxPlanPages <= 0 || settings.MaxReadRetries < 0 || settings.ClaimLeaseSeconds <= 0)
        {
            failures.Add("Maxio timeout/budget/cache/page/lease settings must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
