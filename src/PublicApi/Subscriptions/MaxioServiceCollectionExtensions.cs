using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetRequiredSection(MaxioSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ApiKey), "Maxio:ApiKey is not configured.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Subdomain), "Maxio:Subdomain is not configured.")
            .Validate(
                settings => !string.IsNullOrWhiteSpace(settings.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is not configured.")
            .Validate(
                settings => string.IsNullOrWhiteSpace(settings.BaseUrl) ||
                    Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps,
                "Maxio:BaseUrl must be an absolute HTTPS URL when configured.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioSettings>>().Value;
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
                    Timeout = TimeSpan.FromSeconds(5)
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>(),
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                }
            };

            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<SubscriptionOperationCoordinator>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }
}
