using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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

        // Register Maxio subscription services
        var maxioApiKey = configuration["Maxio:ApiKey"];
        var maxioSubdomain = configuration["Maxio:Subdomain"];
        var productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";

        if (!string.IsNullOrEmpty(maxioApiKey) && !string.IsNullOrEmpty(maxioSubdomain))
        {
            services.AddHttpClient(nameof(MaxioAdvancedBillingClient), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddScoped<ISubscriptionService>(sp =>
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient(nameof(MaxioAdvancedBillingClient));
                var options = new MaxioAdvancedBillingClientOptions
                {
                    BasicAuth = new BasicAuthCredentials
                    {
                        Username = maxioApiKey,
                        Password = "x"
                    },
                    Environment = ServerEnvironment.Us
                };

                // Set the subdomain if BaseUrl override is not provided
                var baseUrl = configuration["Maxio:BaseUrl"];
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    options.Server.Production.Us.BaseUrl = baseUrl;
                }
                else
                {
                    options.Server.Production.Us.Site = maxioSubdomain;
                }

                var client = new MaxioAdvancedBillingClient(httpClient, options);
                var logger = sp.GetRequiredService<IAppLogger<MaxioSubscriptionService>>();
                return new MaxioSubscriptionService(client, productFamilyHandle, logger);
            });
        }
    }
}
