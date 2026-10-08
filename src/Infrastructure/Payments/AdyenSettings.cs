using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Bound from the "Adyen" configuration section. The four credentials/account keys have no defaults: they come
/// from user-secrets, the ADYEN_* environment variables, or a secret store, never from a file in the repository.
/// </summary>
public class AdyenSettings
{
    public const string SectionName = "Adyen";

    /// <summary>Adyen:ApiKey (ADYEN_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Adyen:MerchantAccount (ADYEN_MERCHANT_ACCOUNT).</summary>
    public string MerchantAccount { get; set; } = string.Empty;

    /// <summary>Adyen:Environment (ADYEN_ENVIRONMENT). Only "test" is supported.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Adyen:Currency (ADYEN_CURRENCY) — ISO 4217 code every order is charged in.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>How long one HTTP attempt to Adyen may take. Kept inside the per-request provider budget.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Where Adyen would send a shopper back after a redirect. Derived from baseUrls:webBase at startup.</summary>
    public string ReturnUrl { get; set; } = string.Empty;
}

public class AdyenSettingsValidator : IValidateOptions<AdyenSettings>
{
    public const string SupportedEnvironment = "test";

    private static readonly Regex CurrencyCode = new("^[A-Za-z]{3}$", RegexOptions.Compiled);

    public ValidateOptionsResult Validate(string? name, AdyenSettings options)
    {
        // Name the missing key, never echo a value.
        var failures = new List<string>();
        RequireValue(options.ApiKey, "ApiKey", "ADYEN_API_KEY", failures);
        RequireValue(options.MerchantAccount, "MerchantAccount", "ADYEN_MERCHANT_ACCOUNT", failures);
        RequireValue(options.Environment, "Environment", "ADYEN_ENVIRONMENT", failures);
        RequireValue(options.Currency, "Currency", "ADYEN_CURRENCY", failures);

        if (!string.IsNullOrWhiteSpace(options.Environment) &&
            !string.Equals(options.Environment.Trim(), SupportedEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{AdyenSettings.SectionName}:Environment must be '{SupportedEnvironment}': this build only talks to Adyen's test environment.");
        }

        if (!string.IsNullOrWhiteSpace(options.Currency) && !CurrencyCode.IsMatch(options.Currency.Trim()))
        {
            failures.Add($"{AdyenSettings.SectionName}:Currency must be a three-letter ISO 4217 currency code.");
        }

        if (options.AttemptTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{AdyenSettings.SectionName}:AttemptTimeout must be positive.");
        }

        if (!Uri.TryCreate(options.ReturnUrl, UriKind.Absolute, out var returnUrl) ||
            (returnUrl.Scheme != Uri.UriSchemeHttps && returnUrl.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{AdyenSettings.SectionName}:ReturnUrl must be an absolute http(s) URL (it is derived from baseUrls:webBase).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireValue(string? value, string key, string environmentVariable, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{AdyenSettings.SectionName}:{key} is not configured. Set it with 'dotnet user-secrets', " +
                         $"the {environmentVariable} environment variable, or your secret store before starting the app.");
        }
    }
}
