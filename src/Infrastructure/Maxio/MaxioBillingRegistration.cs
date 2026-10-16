using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioBillingRegistration
{
    public const string HttpClientName = "MaxioAdvancedBilling";
    private const string BasicAuthPassword = "x";

    public static void AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddSingleton(new BillingRuntimeLimits());

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(8);
            })
            .AddHttpMessageHandler(() => new SingleSendWriteGuard())
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var maxio = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
            if (string.IsNullOrWhiteSpace(maxio.ApiKey))
                throw new InvalidOperationException("Maxio:ApiKey is not configured.");
            if (string.IsNullOrWhiteSpace(BasicAuthPassword))
                throw new InvalidOperationException("Maxio basic-auth password is not configured.");
            if (string.IsNullOrWhiteSpace(maxio.Subdomain))
                throw new InvalidOperationException("Maxio:Subdomain is not configured.");

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var environment = SelectEnvironment();
            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = environment,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxio.ApiKey,
                    Password = BasicAuthPassword
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(8),
                    MaxRetries = 2,
                    Delay = TimeSpan.FromMilliseconds(500),
                    MaxJitter = TimeSpan.FromMilliseconds(200)
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            var site = maxio.Subdomain.Trim();
            var baseUrl = string.IsNullOrWhiteSpace(maxio.BaseUrl) ? null : maxio.BaseUrl.Trim();
            if (environment == ServerEnvironment.Eu)
            {
                clientOptions.Server.Production.Eu.Site = site;
                if (baseUrl is not null)
                    clientOptions.Server.Production.Eu.BaseUrl = baseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.Site = site;
                if (baseUrl is not null)
                    clientOptions.Server.Production.Us.BaseUrl = baseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<BillingClaimStore>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
    }

    private static ServerEnvironment SelectEnvironment()
    {
        var region = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        return string.Equals(region, "EU", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu
            : ServerEnvironment.Us;
    }
}
