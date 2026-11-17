using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Finix settings bound from the <c>Finix:</c> configuration section. Values are
/// never hard-coded here; they come from user-secrets, environment variables or
/// the deployment's configuration provider.
/// </summary>
public class FinixOptions
{
    public const string SectionName = "Finix";

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? MerchantId { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }
    public string? BaseUrl { get; set; }

    public bool HasBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);

    public static FinixOptions FromConfiguration(IConfiguration configuration)
    {
        return new FinixOptions
        {
            Username = Value(configuration, "Username", "FINIX_USERNAME"),
            Password = Value(configuration, "Password", "FINIX_PASSWORD"),
            MerchantId = Value(configuration, "MerchantId", "FINIX_MERCHANT_ID"),
            Environment = Value(configuration, "Environment", "FINIX_ENVIRONMENT"),
            Currency = Value(configuration, "Currency", "FINIX_CURRENCY"),
            BaseUrl = Value(configuration, "BaseUrl", "FINIX_BASE_URL")
        };
    }

    public void Validate()
    {
        Require(Username, "Finix:Username", "FINIX_USERNAME");
        Require(Password, "Finix:Password", "FINIX_PASSWORD");
        Require(MerchantId, "Finix:MerchantId", "FINIX_MERCHANT_ID");
        Require(Environment, "Finix:Environment", "FINIX_ENVIRONMENT");
        Require(Currency, "Finix:Currency", "FINIX_CURRENCY");
    }

    private static string? Value(IConfiguration configuration, string key, string environmentVariable)
    {
        var configured = configuration[$"{SectionName}:{key}"];
        return string.IsNullOrWhiteSpace(configured)
            ? configuration[environmentVariable]
            : configured;
    }

    private static void Require(string? value, string key, string environmentVariable)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it via user-secrets, environment variable {environmentVariable}, " +
                "or another configuration provider before starting the application.");
        }
    }
}
