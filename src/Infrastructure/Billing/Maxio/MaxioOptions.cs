using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Values come from user-secrets or the environment: none of them belong in a file that
/// is checked into source control.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>API key used as the Basic-auth user name (the password is the literal "X").</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain, e.g. "acme" for https://acme.chargify.com.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim and the subdomain is only
    /// kept for diagnostics. Use it for EU-hosted sites (https://{site}.ebilling.maxio.com) or a
    /// recording proxy.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How recurring charges are collected for subscriptions this app creates. Leave unset to let
    /// the integration pick a method that does not require a stored payment method
    /// ("remittance" on Relationship Invoicing sites, "invoice" otherwise).
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Per-request timeout. Maxio cuts requests off at 120s.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Retries for throttled (429), transient 5xx and network failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 400;

    /// <summary>The base address the API client should target.</summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var baseUrl = BaseUrl!.Trim();
            if (!baseUrl.EndsWith('/')) baseUrl += "/";
            return new Uri(baseUrl, UriKind.Absolute);
        }

        return new Uri($"https://{Subdomain!.Trim()}.chargify.com/", UriKind.Absolute);
    }
}

/// <summary>Fails fast at start-up when the Maxio settings cannot produce a working client.</summary>
public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return ValidateOptionsResult.Fail(
                "Maxio:ApiKey is not configured. Set it with 'dotnet user-secrets set \"Maxio:ApiKey\" <value>' or the MAXIO_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(options.Subdomain) && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return ValidateOptionsResult.Fail(
                "Maxio:Subdomain is not configured. Set it with 'dotnet user-secrets set \"Maxio:Subdomain\" <value>' or the MAXIO_SITE_SUBDOMAIN environment variable, or supply Maxio:BaseUrl instead.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return ValidateOptionsResult.Fail(
                "Maxio:ProductFamilyHandle is not configured. Set it with 'dotnet user-secrets set \"Maxio:ProductFamilyHandle\" <value>' or the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail($"Maxio:BaseUrl '{options.BaseUrl}' is not an absolute URL.");
        }

        if (options.TimeoutSeconds is < 1 or > 120)
        {
            return ValidateOptionsResult.Fail("Maxio:TimeoutSeconds must be between 1 and 120.");
        }

        if (options.MaxRetryAttempts is < 0 or > 10)
        {
            return ValidateOptionsResult.Fail("Maxio:MaxRetryAttempts must be between 0 and 10.");
        }

        return ValidateOptionsResult.Success;
    }
}
