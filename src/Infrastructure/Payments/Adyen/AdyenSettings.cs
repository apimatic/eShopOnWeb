using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

/// <summary>
/// Bound from the <c>Adyen:</c> configuration section. Values come from configuration only
/// (user-secrets in development, environment / secret store elsewhere) — never from source.
/// </summary>
public class AdyenSettings
{
    public const string SectionName = "Adyen";
    public const string TestEnvironment = "test";
    public const string LiveEnvironment = "live";

    /// <summary><c>Adyen:ApiKey</c> — sent as the X-API-Key header.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary><c>Adyen:MerchantAccount</c>.</summary>
    public string MerchantAccount { get; set; } = string.Empty;

    /// <summary><c>Adyen:Environment</c> — <c>test</c> or <c>live</c>.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary><c>Adyen:Currency</c> — ISO 4217 code every payment is taken in.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// <c>Adyen:CheckoutBaseUrl</c> — optional on <c>test</c> (the SDK's test endpoint is used); required on
    /// <c>live</c>, where it is the merchant's live Checkout API URL from the Adyen Customer Area.
    /// </summary>
    public string? CheckoutBaseUrl { get; set; }

    /// <summary>
    /// <c>Adyen:ReturnUrl</c> — where a shopper would come back after a redirect. Adyen requires the field;
    /// defaults to the storefront base URL (<c>baseUrls:webBase</c>).
    /// </summary>
    public string? ReturnUrl { get; set; }

    public string NormalizedCurrency => Currency.Trim().ToUpperInvariant();

    public bool IsLive => string.Equals(Environment, LiveEnvironment, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Refuses to start the host when the Adyen settings are incomplete, instead of failing on the first
/// payment with a 401. Messages name the missing key and never echo a value.
/// </summary>
public class AdyenSettingsValidator : IValidateOptions<AdyenSettings>
{
    public ValidateOptionsResult Validate(string? name, AdyenSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            failures.Add("Adyen:ApiKey is not configured. Set it via user-secrets, the Adyen__ApiKey / ADYEN_API_KEY environment variable, or your secret store.");
        if (string.IsNullOrWhiteSpace(settings.MerchantAccount))
            failures.Add("Adyen:MerchantAccount is not configured (ADYEN_MERCHANT_ACCOUNT).");
        if (string.IsNullOrWhiteSpace(settings.Currency) || settings.Currency.Trim().Length != 3)
            failures.Add("Adyen:Currency must be a three-letter ISO 4217 currency code (ADYEN_CURRENCY).");

        var environment = settings.Environment?.Trim();
        if (!string.Equals(environment, AdyenSettings.TestEnvironment, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(environment, AdyenSettings.LiveEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Adyen:Environment must be 'test' or 'live' (ADYEN_ENVIRONMENT).");
        }
        else if (settings.IsLive && !IsAbsoluteHttpsUrl(settings.CheckoutBaseUrl))
        {
            failures.Add("Adyen:CheckoutBaseUrl must be set to your live Checkout API URL when Adyen:Environment is 'live'.");
        }

        if (!string.IsNullOrWhiteSpace(settings.CheckoutBaseUrl) && !IsAbsoluteHttpsUrl(settings.CheckoutBaseUrl))
            failures.Add("Adyen:CheckoutBaseUrl must be an absolute https URL.");
        if (string.IsNullOrWhiteSpace(settings.ReturnUrl) || !Uri.TryCreate(settings.ReturnUrl, UriKind.Absolute, out _))
            failures.Add("Adyen:ReturnUrl (or baseUrls:webBase) must be an absolute URL.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAbsoluteHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
