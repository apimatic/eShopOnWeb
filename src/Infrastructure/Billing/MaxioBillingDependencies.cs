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

public static class MaxioBillingDependencies
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
                options => string.IsNullOrWhiteSpace(options.BaseUrl) ||
                           Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                           (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
                "Maxio:BaseUrl must be an absolute HTTP or HTTPS URL when supplied.")
            .Validate(
                options => !options.Subdomain.Contains("//", StringComparison.Ordinal),
                "Maxio:Subdomain must be a site subdomain, not a URL.")
            .ValidateOnStart();

        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
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
                    Timeout = TimeSpan.FromSeconds(8)
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

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionEnrollmentStore, EfSubscriptionEnrollmentStore>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();

        return services;
    }
}
