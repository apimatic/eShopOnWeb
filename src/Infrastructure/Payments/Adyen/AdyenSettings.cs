using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

/// <summary>
/// Bound from the <c>Adyen:</c> configuration section. Values come from user-secrets or the environment —
/// never from a file in the repository.
/// </summary>
public class AdyenSettings
{
    public const string SectionName = "Adyen";

    /// <summary>The only environment the Adyen SDK in use can reach (its single server is Adyen's test host).</summary>
    public const string TestEnvironment = "test";

    /// <summary><c>Adyen:ApiKey</c> — sent as the X-API-Key header.</summary>
    public string? ApiKey { get; set; }

    /// <summary><c>Adyen:MerchantAccount</c> — the merchant account every payment and refund is booked on.</summary>
    public string? MerchantAccount { get; set; }

    /// <summary><c>Adyen:Environment</c> — must be <c>test</c>.</summary>
    public string? Environment { get; set; }

    /// <summary><c>Adyen:Currency</c> — ISO 4217 code every new payment is charged in.</summary>
    public string? Currency { get; set; }

    /// <summary><c>Adyen:TimeoutSeconds</c> (optional, default 20) — how long one Adyen call may take.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// <c>Adyen:ReturnUrl</c> (optional) — where Adyen would send a shopper back after a redirect. Defaults to the
    /// storefront's "my orders" page under <c>baseUrls:webBase</c>.
    /// </summary>
    public string? ReturnUrl { get; set; }

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}

/// <summary>
/// Refuses to start the host with incomplete Adyen settings, naming the missing key and never echoing a value.
/// </summary>
public class AdyenSettingsValidator : IValidateOptions<AdyenSettings>
{
    public ValidateOptionsResult Validate(string? name, AdyenSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            failures.Add($"{AdyenSettings.SectionName}:ApiKey is not configured. Set it via user-secrets or the ADYEN_API_KEY environment variable.");
        if (string.IsNullOrWhiteSpace(settings.MerchantAccount))
            failures.Add($"{AdyenSettings.SectionName}:MerchantAccount is not configured. Set it via user-secrets or the ADYEN_MERCHANT_ACCOUNT environment variable.");
        if (string.IsNullOrWhiteSpace(settings.Environment))
            failures.Add($"{AdyenSettings.SectionName}:Environment is not configured. Set it via user-secrets or the ADYEN_ENVIRONMENT environment variable.");
        else if (!string.Equals(settings.Environment.Trim(), AdyenSettings.TestEnvironment, StringComparison.OrdinalIgnoreCase))
            failures.Add($"{AdyenSettings.SectionName}:Environment must be '{AdyenSettings.TestEnvironment}': the Adyen SDK in use only declares Adyen's test host.");
        if (string.IsNullOrWhiteSpace(settings.Currency))
            failures.Add($"{AdyenSettings.SectionName}:Currency is not configured. Set it via user-secrets or the ADYEN_CURRENCY environment variable.");
        else if (!Money.IsSupportedCurrency(settings.Currency.Trim()))
            failures.Add($"{AdyenSettings.SectionName}:Currency is not a supported ISO 4217 currency code.");
        if (settings.TimeoutSeconds is < 1 or > 120)
            failures.Add($"{AdyenSettings.SectionName}:TimeoutSeconds must be between 1 and 120.");
        if (string.IsNullOrWhiteSpace(settings.ReturnUrl)
            || !Uri.TryCreate(settings.ReturnUrl, UriKind.Absolute, out var returnUrl)
            || (returnUrl.Scheme != Uri.UriSchemeHttps && returnUrl.Scheme != Uri.UriSchemeHttp))
            failures.Add($"{AdyenSettings.SectionName}:ReturnUrl (or baseUrls:webBase) must be an absolute http(s) URL.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
