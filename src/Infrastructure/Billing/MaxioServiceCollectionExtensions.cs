using System;
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
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));

        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddTransient<MaxioStatusCaptureHandler>();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MaxioStatusCaptureHandler>()
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(options, configuration));
        });

        services.AddScoped<IMaxioCallBudget, MaxioCallBudget>();
        services.AddScoped<Microsoft.eShopWeb.ApplicationCore.Interfaces.ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions maxio, IConfiguration configuration)
    {
        var environmentName = configuration["MAXIO_ENVIRONMENT"];
        var isEu = string.Equals(environmentName, "EU", StringComparison.OrdinalIgnoreCase);

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = isEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
            BasicAuth = new BasicAuthCredentials
            {
                Username = maxio.ApiKey,
                Password = "x"
            }
        };

        var baseUrl = maxio.BaseUrl;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            if (isEu)
            {
                clientOptions.Server.Production.Eu.BaseUrl = baseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.BaseUrl = baseUrl;
            }
        }
        else if (!string.IsNullOrWhiteSpace(maxio.Subdomain))
        {
            if (isEu)
            {
                clientOptions.Server.Production.Eu.Site = maxio.Subdomain;
            }
            else
            {
                clientOptions.Server.Production.Us.Site = maxio.Subdomain;
            }
        }

        return clientOptions;
    }
}
