using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public static class MaxioSubscriptionServiceRegistration
{
    private const string ClientName = "MaxioAdvancedBilling";
    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>().Bind(configuration.GetSection("Maxio"))
            .ValidateDataAnnotations()
            .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "Maxio:ApiKey is not configured.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Subdomain), "Maxio:Subdomain is not configured.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.ProductFamilyHandle), "Maxio:ProductFamilyHandle is not configured.")
            .ValidateOnStart();
        var settings = configuration.GetRequiredSection("Maxio").Get<MaxioSettings>() ?? throw new InvalidOperationException("Maxio is not configured.");
        services.AddSingleton(settings);
        services.AddHttpClient(ClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.AddSingleton(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
                Logging = new LoggingOptions { LoggerFactory = loggerFactory, LogRequestBody = false }
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            return new MaxioAdvancedBillingClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName), options);
        });
        services.AddScoped<MaxioSubscriptionService>();
        return services;
    }
}
