using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class Dependencies
{
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

        // Configure Maxio billing
        var maxioConfig = new MaxioConfiguration();
        configuration.GetSection("Maxio").Bind(maxioConfig);
        services.AddSingleton(maxioConfig);

        // Register HttpClient for Maxio
        services.AddHttpClient("Maxio", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        // Register Maxio SDK client
        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio");
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioConfig.ApiKey,
                    Password = "x"
                },
                Environment = maxioConfig.Environment switch
                {
                    "Eu" => ServerEnvironment.Eu,
                    _ => ServerEnvironment.Us
                },
                Server = new ServerOptions
                {
                    Production = new ProductionOptions
                    {
                        Us = new()
                        {
                            Site = maxioConfig.Subdomain
                        }
                    }
                }
            };

            // Override BaseUrl if provided
            if (!string.IsNullOrEmpty(maxioConfig.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = maxioConfig.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        // Register Maxio billing service
        services.AddScoped<IMaxioBillingService, MaxioBillingService>();
    }
}
