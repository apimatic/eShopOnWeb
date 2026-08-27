using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioBillingServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetRequiredSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Maxio:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Subdomain), "Maxio:Subdomain is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is required.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.BaseUrl)
                    || Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps,
                "Maxio:BaseUrl must be an absolute HTTPS URL when supplied.")
            .ValidateOnStart();

        services.AddTransient<MaxioWriteAttemptGuardHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(12);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<MaxioWriteAttemptGuardHandler>();

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

        services.AddScoped<ISubscriptionBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        return services;
    }
}
