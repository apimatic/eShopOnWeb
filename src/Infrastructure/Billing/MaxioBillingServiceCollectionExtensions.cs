using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton(sp =>
            new MaxioReferenceFactory(sp.GetRequiredService<IOptions<MaxioOptions>>().Value));
        services.AddSingleton<SubscriptionKeyedLock>();
        services.AddSingleton<MaxioCallContext>();
        services.AddTransient<MaxioPipelineHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioPipelineHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<MaxioAdvancedBillingClient>>();
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(10),
                    OnRetry = attempt => logger.LogWarning(
                        "Retrying a Maxio request (attempt {AttemptNumber}, delay {Delay}).",
                        attempt.AttemptNumber,
                        attempt.Delay)
                }
            };

            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
