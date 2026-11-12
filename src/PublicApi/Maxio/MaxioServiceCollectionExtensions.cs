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

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MaxioSettings>, MaxioSettingsValidator>();
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.SectionName))
            .ValidateOnStart(); // refuse to boot on missing credentials instead of failing on the first call

        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are captured once here: a rotated API key needs a process restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 2,
                    Delay = TimeSpan.FromSeconds(1)
                },
                Logging = new LoggingOptions
                {
                    // explicit factory: the SDK log env var can never force request-body logging
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                }
            };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl!;
            else
                options.Server.Production.Us.Site = settings.Subdomain!;

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<IMaxioBillingService, MaxioBillingService>();
        return services;
    }
}
