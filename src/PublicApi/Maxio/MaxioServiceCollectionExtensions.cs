using System;
using System.Net.Http;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
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
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(options => string.IsNullOrEmpty(options.BaseUrl) ||
                                 Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                                 (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
                "Maxio:BaseUrl must be an absolute HTTP(S) URL when set.")
            .ValidateOnStart();

        services.AddSingleton<IMaxioAttemptContext, MaxioAttemptContext>();
        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddTransient<MaxioResponseStatusHandler>();

        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .AddHttpMessageHandler<MaxioResponseStatusHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10),
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x",
                },
            };

            if (string.IsNullOrEmpty(settings.BaseUrl))
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<IMaxioSubscriptionGateway, MaxioSubscriptionGateway>();
        return services;
    }
}
