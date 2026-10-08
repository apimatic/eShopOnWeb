using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Bound from the <c>Adyen</c> configuration section. Values come from user-secrets, environment variables
/// (<c>Adyen__ApiKey</c>, …) or a secret store — never from files in the repository.
/// </summary>
public sealed class AdyenSettings
{
    public const string SectionName = "Adyen";
    public const string TestEnvironment = "test";
    public const string LiveEnvironment = "live";

    /// <summary>Adyen:ApiKey — sent as the X-API-Key header.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Adyen:MerchantAccount — the merchant account every payment and refund is booked on.</summary>
    public string? MerchantAccount { get; set; }

    /// <summary>Adyen:Environment — <c>test</c> or <c>live</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Adyen:Currency — ISO-4217 code orders are priced and charged in.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Adyen:CheckoutBaseUrl — optional override of the Checkout API base URL. Required for <c>live</c>,
    /// whose endpoint is specific to the merchant's account; with <c>test</c> the SDK's test endpoint is used.
    /// </summary>
    public string? CheckoutBaseUrl { get; set; }

    public bool IsLive => string.Equals(Environment, LiveEnvironment, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Refuses to start the host when a setting is missing or malformed. Never echoes a value.</summary>
public sealed class AdyenSettingsValidator : IValidateOptions<AdyenSettings>
{
    public ValidateOptionsResult Validate(string? name, AdyenSettings settings)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            failures.Add("Adyen:ApiKey is not configured. Set it via user-secrets, the Adyen__ApiKey environment variable or your secret store.");
        if (string.IsNullOrWhiteSpace(settings.MerchantAccount))
            failures.Add("Adyen:MerchantAccount is not configured.");

        var environment = settings.Environment?.Trim();
        if (!string.Equals(environment, AdyenSettings.TestEnvironment, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environment, AdyenSettings.LiveEnvironment, StringComparison.OrdinalIgnoreCase))
            failures.Add("Adyen:Environment must be 'test' or 'live'.");
        else if (settings.IsLive && string.IsNullOrWhiteSpace(settings.CheckoutBaseUrl))
            failures.Add("Adyen:Environment is 'live' but Adyen:CheckoutBaseUrl (the account-specific live Checkout endpoint) is not configured; refusing to start rather than send live traffic to the test endpoint.");

        if (!string.IsNullOrWhiteSpace(settings.CheckoutBaseUrl)
            && (!Uri.TryCreate(settings.CheckoutBaseUrl, UriKind.Absolute, out var baseUri)
                || (settings.IsLive && baseUri.Scheme != Uri.UriSchemeHttps)))
            failures.Add("Adyen:CheckoutBaseUrl must be an absolute URL (https for live).");

        if (!MinorUnits.IsWellFormedCurrency(settings.Currency?.Trim()))
            failures.Add("Adyen:Currency must be a three-letter upper-case ISO-4217 code, e.g. USD.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
