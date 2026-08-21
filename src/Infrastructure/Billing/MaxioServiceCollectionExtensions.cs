using System;
using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
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
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey) &&
                    !string.IsNullOrWhiteSpace(options.Subdomain) &&
                    !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle are required.")
            .Validate(
                options => Uri.CheckHostName(options.Subdomain) != UriHostNameType.Unknown,
                "Maxio:Subdomain must be a valid DNS host label.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.BaseUrl) ||
                    Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps,
                "Maxio:BaseUrl must be an absolute HTTPS URL when supplied.")
            .ValidateOnStart();

        services.AddTransient<MaxioWriteGuardHandler>();
        services.AddTransient<MaxioResponseStatusHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .AddHttpMessageHandler<MaxioWriteGuardHandler>()
            .AddHttpMessageHandler<MaxioResponseStatusHandler>()
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
                    StatusCodesToRetry = new[]
                    {
                        HttpStatusCode.RequestTimeout,
                        HttpStatusCode.TooManyRequests,
                        HttpStatusCode.InternalServerError,
                        HttpStatusCode.BadGateway,
                        HttpStatusCode.ServiceUnavailable,
                        HttpStatusCode.GatewayTimeout
                    },
                    MaxRetries = 2,
                    Delay = TimeSpan.FromMilliseconds(250),
                    Timeout = TimeSpan.FromSeconds(5),
                    MaxJitter = TimeSpan.FromMilliseconds(100)
                }
            };

            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<Microsoft.eShopWeb.ApplicationCore.Billing.IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddSingleton<AsyncKeyedLocker>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<Microsoft.eShopWeb.ApplicationCore.Billing.ISubscriptionLinkStore, EfSubscriptionLinkStore>();
        services.AddScoped<Microsoft.eShopWeb.ApplicationCore.Billing.ISubscriptionBillingService, SubscriptionBillingService>();

        return services;
    }
}
