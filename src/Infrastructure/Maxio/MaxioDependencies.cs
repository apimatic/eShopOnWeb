using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioDependencies
{
    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        var configSection = configuration.GetSection(MaxioOptions.CONFIG_NAME);
        services.Configure<MaxioOptions>(configSection);
        var maxioOptions = configSection.Get<MaxioOptions>() ?? new MaxioOptions();

        // AddMaxioAdvancedBillingClient resolves the default, unnamed IHttpClientFactory client and
        // builds MaxioAdvancedBillingClient once as a singleton - configure the default client's
        // handler/timeout here so DNS stays fresh behind that long-lived client and a hung provider
        // can't pin a request thread for the (too long) 100s SDK default.
        services.AddHttpClient(Options.DefaultName, c => c.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.Environment = ServerEnvironment.Us;
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = maxioOptions.ApiKey,
                Password = "x"
            };

            if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = maxioOptions.Subdomain;
            }

            options.Retry = options.Retry with { Timeout = TimeSpan.FromSeconds(30) };
        });

        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();
    }
}
