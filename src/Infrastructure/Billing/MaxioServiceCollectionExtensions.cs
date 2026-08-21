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
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetRequiredSection(MaxioOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "Maxio:ApiKey is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Subdomain), "Maxio:Subdomain is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(x => string.IsNullOrWhiteSpace(x.BaseUrl) ||
                           Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out _),
                "Maxio:BaseUrl must be an absolute URL when set.")
            .ValidateOnStart();

        services.AddSingleton<MaxioCallContext>();
        services.AddTransient<MaxioTransportHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioTransportHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
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
                    MaxRetries = 3,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
