using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddTransient<LastStatusCaptureHandler>();
        services.AddTransient<SingleSendWriteHandler>();
        services.AddTransient<MaxioRequestLoggingHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromMinutes(4);
            })
            .AddHttpMessageHandler<MaxioRequestLoggingHandler>()
            .AddHttpMessageHandler<LastStatusCaptureHandler>()
            .AddHttpMessageHandler<SingleSendWriteHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddTransient(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new MaxioAdvancedBillingClient(factory.CreateClient(HttpClientName), CreateClientOptions(options));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    internal static MaxioAdvancedBillingClientOptions CreateClientOptions(MaxioOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new BillingException(503, "Maxio billing is not configured (Maxio:ApiKey is missing).");
        }

        var environment = ResolveEnvironment();
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey,
                Password = "x"
            },
            Retry = RetryOptions.Default() with { MaxRetries = 1 }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            AssignBaseUrl(options, environment, settings.BaseUrl);
        }
        else if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            AssignSite(options, environment, settings.Subdomain);
        }
        else
        {
            throw new BillingException(503, "Maxio billing is not configured (Maxio:Subdomain or Maxio:BaseUrl is required).");
        }

        return options;
    }

    private static ServerEnvironment ResolveEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Eu;
        }

        return ServerEnvironment.Us;
    }

    private static void AssignSite(MaxioAdvancedBillingClientOptions options, ServerEnvironment environment, string site)
    {
        if (environment == ServerEnvironment.Eu)
        {
            options.Server.Production.Eu.Site = site;
        }
        else
        {
            options.Server.Production.Us.Site = site;
        }
    }

    private static void AssignBaseUrl(MaxioAdvancedBillingClientOptions options, ServerEnvironment environment, string baseUrl)
    {
        if (environment == ServerEnvironment.Eu)
        {
            options.Server.Production.Eu.BaseUrl = baseUrl;
        }
        else
        {
            options.Server.Production.Us.BaseUrl = baseUrl;
        }
    }
}
