using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddTransient<MaxioHttpStatusCaptureHandler>();
        services.AddTransient<MaxioWriteOnceHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<MaxioHttpStatusCaptureHandler>()
            .AddHttpMessageHandler<MaxioWriteOnceHandler>();

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return CreateClient(httpClient, settings);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    internal static MaxioAdvancedBillingClient CreateClient(HttpClient httpClient, MaxioOptions settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 1
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,
                Password = "x"
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain;
            options.Server.Production.Eu.Site = settings.Subdomain;
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }
}
