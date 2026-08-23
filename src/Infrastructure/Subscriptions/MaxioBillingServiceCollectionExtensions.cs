using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public static class MaxioBillingServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey) &&
                    !string.IsNullOrWhiteSpace(options.Subdomain) &&
                    !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle are required.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.BaseUrl) ||
                    Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                "Maxio:BaseUrl must be an absolute URL when supplied.");

        services.AddSingleton<MaxioSingleSendGuard>();
        services.AddTransient<MaxioSingleSendHandler>();
        services.AddTransient<MaxioRequestLoggingHandler>();
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<MaxioSingleSendHandler>()
            .AddHttpMessageHandler<MaxioRequestLoggingHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var configured = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(10)
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = configured.ApiKey,
                    Password = "x"
                }
            };

            clientOptions.Server.Production.Us.Site = configured.Subdomain;
            if (!string.IsNullOrWhiteSpace(configured.BaseUrl))
            {
                clientOptions.Server.Production.Us.BaseUrl = configured.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<ISubscriptionBillingStore, SubscriptionBillingStore>();
        services.AddSingleton<ISubscriptionOperationLock, SubscriptionOperationLock>();
        services.AddScoped<IRecurringSubscriptionService, MaxioRecurringSubscriptionService>();
        return services;
    }
}
