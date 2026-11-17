using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Finix;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public static class PaymentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Finix client and the order/payment orchestration service.
    /// Configuration is bound from the "Finix" section.
    /// </summary>
    public static IServiceCollection AddFinixPayments(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FinixSettings>(configuration.GetSection("Finix"));

        services.AddHttpClient<IFinixClient, FinixClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddScoped<IPaymentService, PaymentService>();
        return services;
    }

    /// <summary>
    /// Maps the environment variable names documented for this task onto the
    /// Finix configuration keys. The values themselves are never committed.
    /// </summary>
    public static void AddFinixEnvironmentConfiguration(this IConfigurationBuilder builder)
    {
        var candidates = new Dictionary<string, string?>
        {
            ["Finix:Username"] = Environment.GetEnvironmentVariable("FINIX_USERNAME"),
            ["Finix:Password"] = Environment.GetEnvironmentVariable("FINIX_PASSWORD"),
            ["Finix:MerchantId"] = Environment.GetEnvironmentVariable("FINIX_MERCHANT_ID"),
            ["Finix:Environment"] = Environment.GetEnvironmentVariable("FINIX_ENVIRONMENT"),
            ["Finix:Currency"] = Environment.GetEnvironmentVariable("FINIX_CURRENCY"),
            ["Finix:BaseUrl"] = Environment.GetEnvironmentVariable("FINIX_BASE_URL")
        };

        var values = candidates
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (values.Count > 0)
        {
            builder.AddInMemoryCollection(values);
        }
    }
}
