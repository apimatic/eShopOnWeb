using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using FinixCurrencyEnum = FinixApiReference.Models.Enums.Currency;

namespace Microsoft.eShopWeb.PublicApi.Finix;

/// <summary>
/// Settings bound from the <c>Finix</c> configuration section. No value is hard-coded here:
/// every deployment supplies its own account through configuration or environment variables.
/// </summary>
public class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public FinixCurrencyEnum CurrencyValue =>
        FinixCurrencyEnum.TryGetKnownValue(Currency, out var currency) ? currency! : FinixCurrencyEnum.Usd;
}

public sealed class FinixOptionsValidator : IValidateOptions<FinixOptions>
{
    public ValidateOptionsResult Validate(string? name, FinixOptions options)
    {
        var errors = new List<string>();

        Require(errors, options.Username, "Finix:Username");
        Require(errors, options.Password, "Finix:Password");
        Require(errors, options.MerchantId, "Finix:MerchantId");
        Require(errors, options.Environment, "Finix:Environment");
        Require(errors, options.Currency, "Finix:Currency");

        if (!string.IsNullOrWhiteSpace(options.Environment) &&
            !string.Equals(options.Environment, "sandbox", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Finix:Environment must be 'sandbox' (or the SDK's equivalent 'production'); " +
                       "this SDK build declares no live server.");
        }

        if (!string.IsNullOrWhiteSpace(options.Currency) &&
            !FinixCurrencyEnum.TryGetKnownValue(options.Currency, out _))
        {
            errors.Add("Finix:Currency must be one of the currencies declared by the Finix SDK (USD, CAD).");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("Finix:BaseUrl must be an absolute http(s) URL when it is set.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static void Require(List<string> errors, string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is not configured. Supply it through environment variables, " +
                       "user-secrets, or your secret store before starting the app.");
        }
    }
}
