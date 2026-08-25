using System;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
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
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }

        ConfigurePayPal(configuration, services);
    }

    private static void ConfigurePayPal(IConfiguration configuration, IServiceCollection services)
    {
        const string clientName = "PayPalServerSdk";

        services.AddHttpClient(clientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient(clientName);

            var options = new PayPalServerSdkClientOptions
            {
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = configuration["PayPal:ClientId"] ?? throw new InvalidOperationException("PayPal:ClientId is required"),
                    ClientSecret = configuration["PayPal:ClientSecret"] ?? throw new InvalidOperationException("PayPal:ClientSecret is required")
                },
                Environment = ServerEnvironment.Sandbox,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(15)
                }
            };

            var baseUrl = configuration["PayPal:BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = baseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();
    }
}
