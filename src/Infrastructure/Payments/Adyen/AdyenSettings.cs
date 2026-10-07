using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Adyen;

/// <summary>
/// Adyen settings, bound from the <c>Adyen:</c> configuration section. Values come from user-secrets in
/// development and from the deployment's configuration sources elsewhere; none is hard-coded.
/// </summary>
public sealed class AdyenSettings
{
    public const string SectionName = "Adyen";

    /// <summary>The only environment the bundled Adyen SDK declares hosts for.</summary>
    public const string TestEnvironment = "test";

    public string ApiKey { get; set; } = string.Empty;
    public string MerchantAccount { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    // Never print the API key, even by accident.
    public override string ToString() =>
        $"AdyenSettings {{ MerchantAccount = {MerchantAccount}, Environment = {Environment}, Currency = {Currency}, ApiKey = *** }}";
}

/// <summary>
/// Refuses to let the host start with missing or unusable Adyen settings. Messages name the key, never the value.
/// </summary>
public sealed class AdyenSettingsValidator : IValidateOptions<AdyenSettings>
{
    public ValidateOptionsResult Validate(string? name, AdyenSettings options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            failures.Add($"{AdyenSettings.SectionName}:ApiKey is not configured. Set it via user-secrets or your secret store.");
        if (string.IsNullOrWhiteSpace(options.MerchantAccount))
            failures.Add($"{AdyenSettings.SectionName}:MerchantAccount is not configured.");
        if (!string.Equals(options.Environment?.Trim(), AdyenSettings.TestEnvironment, StringComparison.OrdinalIgnoreCase))
            failures.Add($"{AdyenSettings.SectionName}:Environment must be '{AdyenSettings.TestEnvironment}'; " +
                         "the bundled Adyen SDK only declares Adyen's test hosts.");
        if (string.IsNullOrWhiteSpace(options.Currency) || options.Currency.Trim().Length != 3 || !options.Currency.Trim().All(char.IsLetter))
            failures.Add($"{AdyenSettings.SectionName}:Currency must be a three-letter ISO 4217 currency code.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
