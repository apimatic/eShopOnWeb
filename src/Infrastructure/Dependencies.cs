using System;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Servers;

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

        // Configure PayPal
        var paypalSettings = new PayPalSettings();
        configuration.GetSection(PayPalSettings.SectionName).Bind(paypalSettings);

        services.AddHttpClient<PayPalServerSdkClient>((client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(100);
        });

        services.AddSingleton(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(PayPalServerSdkClient));

            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox
            };

            // Set OAuth2 credentials
            options.Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials
            {
                ClientId = paypalSettings.ClientId,
                ClientSecret = paypalSettings.ClientSecret
            };

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentService>(sp =>
        {
            var client = sp.GetRequiredService<PayPalServerSdkClient>();
            var logger = sp.GetRequiredService<ILogger<PayPalPaymentService>>();
            return new PayPalPaymentService(client, paypalSettings.Currency, logger);
        });
    }
}
