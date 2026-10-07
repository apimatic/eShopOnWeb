using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Payments.Adyen;

/// <summary>
/// Bound from the <c>Adyen:</c> configuration section. Values come from the environment / user-secrets / a secret
/// store — never from a file in this repository.
/// </summary>
public sealed class AdyenSettings
{
    public const string SectionName = "Adyen";

    /// <summary>The only <see cref="Environment"/> this build supports: the SDK declares Adyen's test endpoints only.</summary>
    public const string TestEnvironment = "test";

    /// <summary><c>Adyen:ApiKey</c> — sent in the X-API-Key header.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary><c>Adyen:MerchantAccount</c>.</summary>
    public string MerchantAccount { get; set; } = "";

    /// <summary><c>Adyen:Environment</c> — must be <c>test</c>.</summary>
    public string Environment { get; set; } = "";

    /// <summary><c>Adyen:Currency</c> — ISO 4217 code every order is charged in.</summary>
    public string Currency { get; set; } = "";

    /// <summary>
    /// Where Adyen would send a shopper after a redirect. Required by the payment request even though this
    /// direct card flow never redirects; defaults to the storefront's base URL.
    /// </summary>
    public string ReturnUrl { get; set; } = "";
}

/// <summary>
/// Refuses to start the host when a setting is missing or invalid, naming the key and never echoing a value.
/// </summary>
public sealed class AdyenSettingsValidator : IValidateOptions<AdyenSettings>
{
    private static readonly Regex CurrencyCode = new("^[A-Z]{3}$", RegexOptions.Compiled);

    public ValidateOptionsResult Validate(string? name, AdyenSettings options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add("Adyen:ApiKey is not configured. Set it from ADYEN_API_KEY (user-secrets, environment variable Adyen__ApiKey, or your secret store).");
        if (string.IsNullOrWhiteSpace(options.MerchantAccount))
            failures.Add("Adyen:MerchantAccount is not configured. Set it from ADYEN_MERCHANT_ACCOUNT.");
        if (string.IsNullOrWhiteSpace(options.Environment))
            failures.Add("Adyen:Environment is not configured. Set it from ADYEN_ENVIRONMENT.");
        else if (!string.Equals(options.Environment.Trim(), AdyenSettings.TestEnvironment, StringComparison.OrdinalIgnoreCase))
            failures.Add("Adyen:Environment must be 'test': the Adyen SDK this build uses declares only Adyen's test endpoints.");
        if (string.IsNullOrWhiteSpace(options.Currency))
            failures.Add("Adyen:Currency is not configured. Set it from ADYEN_CURRENCY.");
        else if (!CurrencyCode.IsMatch(options.Currency.Trim()))
            failures.Add("Adyen:Currency must be a three-letter ISO 4217 code in upper case, for example USD.");
        if (!Uri.TryCreate(options.ReturnUrl, UriKind.Absolute, out var returnUrl)
            || (returnUrl.Scheme != Uri.UriSchemeHttps && returnUrl.Scheme != Uri.UriSchemeHttp))
            failures.Add("Adyen:ReturnUrl (defaults to baseUrls:webBase) must be an absolute http(s) URL.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
