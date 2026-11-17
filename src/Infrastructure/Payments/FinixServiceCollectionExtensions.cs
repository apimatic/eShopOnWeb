using System;
using System.Collections.Generic;
using System.Linq;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class FinixServiceCollectionExtensions
{
    private const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    private const string LiveBaseUrl = "https://finix.live-payments-api.com";

    public static IServiceCollection AddFinixPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var mergedConfiguration = MergeFinixEnvironment(configuration);
        var section = mergedConfiguration.GetSection("Finix");
        var settings = section.Get<FinixOptions>() ?? new FinixOptions();

        Validate(settings);

        services.Configure<FinixOptions>(section);
        services.AddFinixApiReferenceClient(options =>
        {
            options.Server.Default.Production.BaseUrl = ResolveBaseUrl(settings);
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = settings.Username,
                Password = settings.Password
            };
            options.Logging = options.Logging with
            {
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
                RedactedKeys = options.Logging.RedactedKeys
                    .Concat(new[] { "number", "card_number", "security_code", "cvv", "card" })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        });

        services.AddSingleton<FinixGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        return services;
    }

    private static string ResolveBaseUrl(FinixOptions settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl;
        }

        return string.Equals(settings.Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }

    private static void Validate(FinixOptions settings)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.Username)) missing.Add("Finix:Username");
        if (string.IsNullOrWhiteSpace(settings.Password)) missing.Add("Finix:Password");
        if (string.IsNullOrWhiteSpace(settings.MerchantId)) missing.Add("Finix:MerchantId");
        if (string.IsNullOrWhiteSpace(settings.Environment)) missing.Add("Finix:Environment");
        if (string.IsNullOrWhiteSpace(settings.Currency)) missing.Add("Finix:Currency");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Finix configuration is incomplete. Missing: {string.Join(", ", missing)}. " +
                "Set these keys in the Finix configuration section (the FINIX_* environment variables are used as defaults).");
        }
    }

    private static IConfiguration MergeFinixEnvironment(IConfiguration configuration)
    {
        var environmentValues = new Dictionary<string, string?>
        {
            ["Finix:Username"] = Environment.GetEnvironmentVariable("FINIX_USERNAME"),
            ["Finix:Password"] = Environment.GetEnvironmentVariable("FINIX_PASSWORD"),
            ["Finix:MerchantId"] = Environment.GetEnvironmentVariable("FINIX_MERCHANT_ID"),
            ["Finix:Environment"] = Environment.GetEnvironmentVariable("FINIX_ENVIRONMENT"),
            ["Finix:Currency"] = Environment.GetEnvironmentVariable("FINIX_CURRENCY")
        };

        var overrides = environmentValues
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (overrides.Count == 0)
        {
            return configuration;
        }

        return new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(overrides)
            .Build();
    }
}
