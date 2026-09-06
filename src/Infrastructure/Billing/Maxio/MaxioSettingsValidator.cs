using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Fails fast on misconfiguration rather than letting the first shopper discover it as a 500.
/// </summary>
public class MaxioSettingsValidator : IValidateOptions<MaxioSettings>
{
    public ValidateOptionsResult Validate(string? name, MaxioSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is required. Set it in user-secrets or the environment; never commit it.");
        }

        var hasBaseUrl = !string.IsNullOrWhiteSpace(options.BaseUrl);
        if (!hasBaseUrl && string.IsNullOrWhiteSpace(options.Subdomain))
        {
            failures.Add("Maxio:Subdomain is required unless Maxio:BaseUrl is supplied.");
        }

        if (hasBaseUrl && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            failures.Add($"Maxio:BaseUrl must be an absolute URL. Found '{options.BaseUrl}'.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is required; it selects the product family published as subscription plans.");
        }

        if (!MaxioEnvironments.IsKnown(options.Environment))
        {
            failures.Add($"Maxio:Environment must be '{MaxioEnvironments.US}' or '{MaxioEnvironments.EU}'. Found '{options.Environment}'.");
        }

        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            failures.Add("Maxio:RequestTimeout must be greater than zero.");
        }

        if (options.MaxRetryAttempts < 0)
        {
            failures.Add("Maxio:MaxRetryAttempts must not be negative.");
        }

        if (string.IsNullOrWhiteSpace(options.ReferencePrefix))
        {
            failures.Add("Maxio:ReferencePrefix must not be empty; it namespaces the references this app writes.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
