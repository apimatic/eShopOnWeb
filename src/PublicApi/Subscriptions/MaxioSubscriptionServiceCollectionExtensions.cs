using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class MaxioSubscriptionServiceCollectionExtensions
{
    private const string MaxioHttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetRequiredSection(MaxioSettings.SectionName))
            .ValidateDataAnnotations()
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ApiKey)
                && !string.IsNullOrWhiteSpace(settings.Subdomain)
                && !string.IsNullOrWhiteSpace(settings.ProductFamilyHandle), "Maxio configuration is incomplete.")
            .ValidateOnStart();

        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddHttpClient(MaxioHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<MaxioWriteOnceHandler>();

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Retry = RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(10) }
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(MaxioHttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<IMaxioSubscriptionGateway, MaxioSubscriptionGateway>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        return services;
    }
}
