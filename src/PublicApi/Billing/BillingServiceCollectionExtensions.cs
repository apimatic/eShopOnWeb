using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class BillingServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "Maxio:ApiKey is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Subdomain),
                "Maxio:Subdomain is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is required.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.BaseUrl) ||
                           Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                "Maxio:BaseUrl must be an absolute URL when set.")
            .ValidateOnStart();

        services.AddSingleton<IMaxioResponseContext, MaxioResponseContext>();
        services.AddSingleton<IMaxioWriteGuard, MaxioWriteGuard>();
        services.AddTransient<MaxioResponseStatusHandler>();
        services.AddTransient<MaxioWriteOnceHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioResponseStatusHandler>()
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
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
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
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

            var httpClient = serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ICurrentBillingCustomer, CurrentBillingCustomer>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
        return services;
    }
}
