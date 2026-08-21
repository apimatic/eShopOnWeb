using System;
using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class MaxioServiceCollectionExtensions
{
    private const string ClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddHttpClient(ClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = configuration.GetRequiredSection(MaxioOptions.SectionName).Get<MaxioOptions>()
                ?? throw new InvalidOperationException("The Maxio configuration section is missing.");
            Validate(settings);

            var region = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
            var environment = region?.ToUpperInvariant() switch
            {
                "US" => ServerEnvironment.Us,
                "EU" => ServerEnvironment.Eu,
                _ => throw new InvalidOperationException(
                    "MAXIO_ENVIRONMENT must be set to US or EU.")
            };

            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Delay = TimeSpan.FromMilliseconds(500),
                    MaxJitter = TimeSpan.FromMilliseconds(250),
                    Timeout = TimeSpan.FromSeconds(8),
                    StatusCodesToRetry = new[]
                    {
                        HttpStatusCode.RequestTimeout,
                        HttpStatusCode.TooManyRequests,
                        HttpStatusCode.InternalServerError,
                        HttpStatusCode.BadGateway,
                        HttpStatusCode.ServiceUnavailable,
                        HttpStatusCode.GatewayTimeout
                    },
                    HttpMethodsToRetry = new[]
                    {
                        HttpMethod.Get,
                        HttpMethod.Head,
                        HttpMethod.Put,
                        HttpMethod.Options
                    }
                }
            };

            if (environment == ServerEnvironment.Us)
            {
                if (string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Us.Site = settings.Subdomain;
                }
                else
                {
                    options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Eu.Site = settings.Subdomain;
                }
                else
                {
                    options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
                }
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<SubscriptionOperationLock>();
        services.AddScoped<MaxioBillingGateway>();
        services.AddScoped<SubscriptionBillingService>();
        return services;
    }

    private static void Validate(MaxioOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl) && string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }
    }
}
