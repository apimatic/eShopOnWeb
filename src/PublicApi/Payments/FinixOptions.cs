using System;
using System.Collections.Generic;
using FinixApiReference.Models.Enums;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Finix settings bound from the <c>Finix</c> configuration section.
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

    public bool IsLive => string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase);

    public string CurrencyCode => Currency.Trim().ToUpperInvariant();

    public FinixApiReference.Models.Enums.Currency ResolveCurrency()
    {
        if (!FinixApiReference.Models.Enums.Currency.TryGetKnownValue(CurrencyCode, out var currency))
        {
            throw new InvalidOperationException(
                $"Finix:Currency '{Currency}' is not a currency this SDK declares. Supported values: " +
                string.Join(", ", FinixApiReference.Models.Enums.Currency.GetKnownValues()));
        }

        return currency;
    }
}

/// <summary>
/// Loads the FINIX_* environment variables into the Finix configuration section when the
/// section key is not already supplied by a higher-priority configuration source.
/// </summary>
public static class FinixConfigurationExtensions
{
    public static void AddFinixEnvironmentVariables(this IConfigurationBuilder configuration)
    {
        var current = configuration as IConfiguration ?? configuration.Build();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AddIfMissing(current, values, "Username", "FINIX_USERNAME");
        AddIfMissing(current, values, "Password", "FINIX_PASSWORD");
        AddIfMissing(current, values, "MerchantId", "FINIX_MERCHANT_ID");
        AddIfMissing(current, values, "Environment", "FINIX_ENVIRONMENT");
        AddIfMissing(current, values, "Currency", "FINIX_CURRENCY");

        if (values.Count > 0)
        {
            configuration.AddInMemoryCollection(values);
        }
    }

    private static void AddIfMissing(
        IConfiguration configuration,
        IDictionary<string, string?> values,
        string sectionKey,
        string environmentVariable)
    {
        var fullKey = $"{FinixOptions.SectionName}:{sectionKey}";
        if (!string.IsNullOrWhiteSpace(configuration[fullKey]))
        {
            return;
        }

        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[fullKey] = value;
        }
    }
}
