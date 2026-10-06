using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class Dependencies
{
    private const string MaxioHttpClientName = "MaxioAdvancedBilling";

    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        bool useOnlyInMemoryDatabase = false;
        if (configuration["UseOnlyInMemoryDatabase"] != null)
        {
            useOnlyInMemoryDatabase = bool.Parse(configuration["UseOnlyInMemoryDatabase"]!);
        }

        if (useOnlyInMemoryDatabase)
        {
            services.AddDbContext<CatalogContext>(c =>
               c.UseInMemoryDatabase("Catalog"));

            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseInMemoryDatabase("Identity"));
        }
        else
        {
            // use real database
            // Requires LocalDB which can be installed with SQL Server Express 2016
            // https://www.microsoft.com/en-us/download/details.aspx?id=54284
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

            // Add Identity DbContext
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }

        AddMaxioBilling(configuration, services);
    }

    private static void AddMaxioBilling(IConfiguration configuration, IServiceCollection services)
    {
        services.AddSingleton(MaxioOptions.Load(configuration));

        services.AddTransient<MaxioLastStatusCodeHandler>();

        services.AddHttpClient(MaxioHttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<MaxioLastStatusCodeHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<MaxioAdvancedBillingClient>(serviceProvider =>
        {
            var maxioOptions = serviceProvider.GetRequiredService<MaxioOptions>();
            var httpClient = serviceProvider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(MaxioHttpClientName);

            var clientOptions = new MaxioAdvancedBillingClientOptions
            {
                Environment = maxioOptions.ServerEnvironment,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(15)
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioOptions.ApiKey,
                    Password = "x"
                }
            };

            if (maxioOptions.ServerEnvironment == MaxioAdvancedBilling.Servers.ServerEnvironment.Eu)
            {
                clientOptions.Server.Production.Eu.Site = maxioOptions.Subdomain;
                if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
                {
                    clientOptions.Server.Production.Eu.BaseUrl = maxioOptions.BaseUrl;
                }
            }
            else
            {
                clientOptions.Server.Production.Us.Site = maxioOptions.Subdomain;
                if (!string.IsNullOrWhiteSpace(maxioOptions.BaseUrl))
                {
                    clientOptions.Server.Production.Us.BaseUrl = maxioOptions.BaseUrl;
                }
            }

            return new MaxioAdvancedBillingClient(httpClient, clientOptions);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
    }
}
