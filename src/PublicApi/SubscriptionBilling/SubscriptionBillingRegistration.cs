using System;
using System.Net.Http;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public static class SubscriptionBillingRegistration
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddSubscriptionBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection(MaxioOptions.SectionName);
        services.AddOptions<MaxioOptions>()
            .Bind(section)
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Maxio:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Subdomain), "Maxio:Subdomain is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ProductFamilyHandle is required.")
            .Validate(
                options => string.IsNullOrEmpty(options.BaseUrl) ||
                    Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                "Maxio:BaseUrl must be an absolute URL when set.")
            .ValidateOnStart();

        services.AddSingleton<MaxioTransportContext>();
        services.AddTransient<MaxioTransportHandler>();
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<MaxioTransportHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
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
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrEmpty(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<SubscriptionIntentCoordinator>();
        services.AddScoped<IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionApplicationService, SubscriptionApplicationService>();
        services.AddHttpContextAccessor();
        return services;
    }
}
