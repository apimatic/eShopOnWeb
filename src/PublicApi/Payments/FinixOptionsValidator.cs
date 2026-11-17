using System;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class FinixOptionsValidator : IValidateOptions<FinixOptions>
{
    public ValidateOptionsResult Validate(string? name, FinixOptions options)
    {
        try
        {
            ValidateOrThrow(options);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }

    public static void ValidateOrThrow(FinixOptions options)
    {
        Require(options.Username, "Finix:Username", "FINIX_USERNAME");
        Require(options.Password, "Finix:Password", "FINIX_PASSWORD");
        Require(options.MerchantId, "Finix:MerchantId", "FINIX_MERCHANT_ID");
        Require(options.Environment, "Finix:Environment", "FINIX_ENVIRONMENT");
        Require(options.Currency, "Finix:Currency", "FINIX_CURRENCY");

        if (!string.Equals(options.Environment, "sandbox", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Environment, "production", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Environment, "live", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Finix:Environment must be 'sandbox' or 'live'.");
        }

        _ = options.ResolveCurrency();

        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException("Finix:BaseUrl must be an absolute HTTP(S) URL.");
        }
    }

    private static void Require(string? value, string key, string environmentVariable)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it through the Finix configuration section, " +
                $"user-secrets, or the {environmentVariable} environment variable before starting the app.");
        }
    }
}
