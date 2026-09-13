using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

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
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }

        // Maxio Advanced Billing SDK
        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions();

            var apiKey = configuration["Maxio:ApiKey"];
            var subdomain = configuration["Maxio:Subdomain"];
            var environment = configuration["Maxio:Environment"];
            var baseUrl = configuration["Maxio:BaseUrl"];

            options.BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey ?? string.Empty,
                Password = "x"
            };

            options.Environment = string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us;

            if (!string.IsNullOrEmpty(subdomain))
            {
                options.Server.Production.Us.Site = subdomain;
            }

            if (!string.IsNullOrEmpty(baseUrl))
            {
                options.Server.Production.Us.BaseUrl = baseUrl;
            }

            options.Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(30),
                MaxRetries = 2
            };

            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<IMaxioCustomerService, MaxioCustomerService>();
        services.AddScoped<IMaxioSubscriptionService>(sp =>
        {
            var client = sp.GetRequiredService<MaxioAdvancedBillingClient>();
            var customerService = sp.GetRequiredService<IMaxioCustomerService>();
            var productFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
            return new MaxioSubscriptionService(client, customerService, productFamilyHandle);
        });
    }
}
