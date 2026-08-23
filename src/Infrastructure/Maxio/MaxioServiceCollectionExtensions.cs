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

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var configuredEnvironment = configuration["MaxioEnvironment"];
        if (!string.Equals(configuredEnvironment, "US", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(configuredEnvironment, "EU", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MaxioEnvironment must be either US or EU.");
        }

        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetRequiredSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey) &&
                                 !string.IsNullOrWhiteSpace(options.Subdomain) &&
                                 !string.IsNullOrWhiteSpace(options.ProductFamilyHandle),
                "Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle are required.")
            .Validate(options => string.IsNullOrWhiteSpace(options.BaseUrl) ||
                                 Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                                 (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
                "Maxio:BaseUrl must be an absolute HTTP(S) URL when supplied.")
            .ValidateOnStart();

        services.AddSingleton<MaxioCallContext>();
        services.AddSingleton<AsyncKeyedLock>();
        services.AddTransient<MaxioTransportHandler>();

        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(8))
            .AddHttpMessageHandler<MaxioTransportHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var useUsEnvironment = string.Equals(configuredEnvironment, "US", StringComparison.OrdinalIgnoreCase);
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = useUsEnvironment ? ServerEnvironment.Us : ServerEnvironment.Eu,
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

            if (useUsEnvironment)
            {
                options.Server.Production.Us.Site = settings.Subdomain;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                }
            }
            else
            {
                options.Server.Production.Eu.Site = settings.Subdomain;
                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
                }
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }
}
