using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioSettings.CONFIG_SECTION_NAME);
        services.AddOptions<MaxioSettings>()
            .Bind(section)
            .ValidateDataAnnotations();

        services.AddHttpClient(HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(20),
                    MaxRetries = 1
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey ?? string.Empty,
                    Password = "x"
                }
            };
            options.Server.Production.Us.Site = settings.Subdomain ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<SubscriberLock>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();

        return services;
    }
}
