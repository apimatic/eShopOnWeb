using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpContextAccessor();
        services.AddTransient<MaxioStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        })
        .AddHttpMessageHandler<MaxioStatusCaptureHandler>()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var us = new ProductionOptions.UsOptions
            {
                Site = options.Subdomain
            };
            if (!string.IsNullOrEmpty(options.BaseUrl))
            {
                us.BaseUrl = options.BaseUrl;
            }

            var sdkOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                },
                Server = new ServerOptions
                {
                    Production = new ProductionOptions
                    {
                        Us = us
                    }
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = options.ApiKey,
                    Password = "x"
                }
            };

            return new MaxioAdvancedBillingClient(httpClient, sdkOptions);
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
