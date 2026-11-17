using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Finix;

/// <summary>
/// Settings bound from the "Finix" configuration section. Every value is supplied by the host;
/// none is baked into the build. Environment variables are accepted as a fallback so the same
/// build can run against another Finix account without a configuration file change.
/// </summary>
public sealed class FinixOptions
{
    public const string SectionName = "Finix";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public static FinixOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        var options = new FinixOptions
        {
            Username = FirstNonBlank(section["Username"], System.Environment.GetEnvironmentVariable("FINIX_USERNAME")),
            Password = FirstNonBlank(section["Password"], System.Environment.GetEnvironmentVariable("FINIX_PASSWORD")),
            MerchantId = FirstNonBlank(section["MerchantId"], System.Environment.GetEnvironmentVariable("FINIX_MERCHANT_ID")),
            Environment = FirstNonBlank(section["Environment"], System.Environment.GetEnvironmentVariable("FINIX_ENVIRONMENT")),
            Currency = FirstNonBlank(section["Currency"], System.Environment.GetEnvironmentVariable("FINIX_CURRENCY")),
            BaseUrl = FirstNonBlank(section["BaseUrl"], null)
        };

        options.Validate();
        return options;
    }

    public void Validate()
    {
        Require(nameof(Username), Username);
        Require(nameof(Password), Password);
        Require(nameof(MerchantId), MerchantId);

        var environment = Environment?.Trim().ToLowerInvariant();
        if (environment is not ("sandbox" or "live"))
        {
            throw new InvalidOperationException(
                "Finix:Environment is not configured. Set it to 'sandbox' or 'live' via FINIX_ENVIRONMENT.");
        }
        Environment = environment;

        var currency = Currency?.Trim().ToUpperInvariant();
        if (currency is not ("USD" or "CAD"))
        {
            throw new InvalidOperationException(
                "Finix:Currency is not configured. Set it to 'USD' or 'CAD' via FINIX_CURRENCY.");
        }
        Currency = currency;

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException(
                    "Finix:BaseUrl must be an absolute http or https URL when it is set.");
            }

            BaseUrl = BaseUrl.Trim().TrimEnd('/');
        }
    }

    public string ResolveBaseUrl() => !string.IsNullOrWhiteSpace(BaseUrl)
        ? BaseUrl!
        : Environment == "live"
            ? "https://finix.live-payments-api.com"
            : "https://finix.sandbox-payments-api.com";

    private static void Require(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Finix:{key} is not configured. Set it via user-secrets, the Finix configuration section, " +
                $"or the matching FINIX_* environment variable before starting the app.");
        }
    }

    private static string FirstNonBlank(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first!.Trim() : second?.Trim() ?? string.Empty;
}
