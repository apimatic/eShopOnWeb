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

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = ReadOptions(configuration);
        services.AddSingleton<IOptions<MaxioOptions>>(Options.Create(settings));

        services.AddTransient<SingleAttemptWriteHandler>();
        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(100);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .AddHttpMessageHandler<SingleAttemptWriteHandler>();

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            return new MaxioAdvancedBillingClient(factory.CreateClient(HttpClientName), BuildClientOptions(options));
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
        return services;
    }

    private static MaxioOptions ReadOptions(IConfiguration configuration)
    {
        return new MaxioOptions
        {
            ApiKey = configuration["Maxio:ApiKey"] ?? string.Empty,
            Subdomain = configuration["Maxio:Subdomain"] ?? string.Empty,
            ProductFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? string.Empty,
            BaseUrl = configuration["Maxio:BaseUrl"]
        };
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions settings)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(30)
            },
            BasicAuth = new BasicAuthCredentials
            {
                Username = settings.ApiKey ?? string.Empty,
                Password = "x"
            }
        };

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? null : settings.BaseUrl.Trim();
        if (baseUrl is not null)
        {
            clientOptions.Server.Production.Us.BaseUrl = baseUrl;
            if (baseUrl.Contains("{site}", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                clientOptions.Server.Production.Us.Site = settings.Subdomain;
            }
        }
        else if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            clientOptions.Server.Production.Us.Site = settings.Subdomain;
        }

        return clientOptions;
    }
}
