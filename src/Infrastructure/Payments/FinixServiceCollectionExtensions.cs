using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class FinixServiceCollectionExtensions
{
    public static IServiceCollection AddFinixPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(FinixOptions.SectionName);

        // Deployment credentials come from the environment. User-secrets or
        // appsettings are used as a local fallback, never as a hard-coded value.
        var options = new FinixOptions
        {
            Username = FirstNonEmpty("FINIX_USERNAME", section["Username"]),
            Password = FirstNonEmpty("FINIX_PASSWORD", section["Password"]),
            MerchantId = FirstNonEmpty("FINIX_MERCHANT_ID", section["MerchantId"]),
            Environment = FirstNonEmpty("FINIX_ENVIRONMENT", section["Environment"]),
            Currency = FirstNonEmpty("FINIX_CURRENCY", section["Currency"]),
            BaseUrl = FirstNonEmptyOrNull("FINIX_BASE_URL", section["BaseUrl"])
        };

        services.AddSingleton(options);
        services.AddSingleton<IFinixClient>(sp =>
            new FinixClient(new HttpClient(), options, sp.GetRequiredService<ILogger<FinixClient>>()));
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();

        return services;
    }

    private static string FirstNonEmpty(string environmentVariable, string? configuredValue)
    {
        return FirstNonEmptyOrNull(environmentVariable, configuredValue) ?? string.Empty;
    }

    private static string? FirstNonEmptyOrNull(string environmentVariable, string? configuredValue)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return string.IsNullOrWhiteSpace(configuredValue) ? null : configuredValue;
    }
}
