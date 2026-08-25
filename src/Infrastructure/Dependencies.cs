using System;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
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

        // PayPal SDK client
        var paypalBaseUrl = configuration["PayPal:BaseUrl"];
        // Only Sandbox is a named environment in this SDK; production traffic uses BaseUrl override.
        var paypalEnv = ServerEnvironment.Sandbox;

        const string PayPalClientName = "PayPalSDK";

        services.AddHttpClient(PayPalClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            var socketsHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            if (!string.IsNullOrWhiteSpace(paypalBaseUrl))
                return new BaseUrlOverrideHandler(paypalBaseUrl) { InnerHandler = socketsHandler };
            return socketsHandler;
        });

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient(PayPalClientName);
            var options = new PayPalServerSdkClientOptions
            {
                Environment = paypalEnv,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = configuration["PayPal:ClientId"]
                        ?? throw new InvalidOperationException("PayPal:ClientId is not configured."),
                    ClientSecret = configuration["PayPal:ClientSecret"]
                        ?? throw new InvalidOperationException("PayPal:ClientSecret is not configured.")
                }
            };
            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPaymentService, PayPalPaymentService>();
    }
}
