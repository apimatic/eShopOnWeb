using System;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
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
    private const string PayPalHttpClientName = "PayPal";

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

        ConfigurePayPal(configuration, services);
    }

    private static void ConfigurePayPal(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.CONFIG_NAME));
        var payPalSettings = configuration.GetSection(PayPalSettings.CONFIG_NAME).Get<PayPalSettings>()
            ?? new PayPalSettings();

        // Named client keeps the PayPal timeout/handler pipeline off the shared default client.
        services.AddHttpClient(PayPalHttpClientName, c =>
            {
                // Per-attempt backstop against a hung provider (the default 100s is an outage).
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; keep DNS fresh behind it.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            if (string.IsNullOrEmpty(payPalSettings.ClientId) || string.IsNullOrEmpty(payPalSettings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal credentials are missing. Set PayPal:ClientId and PayPal:ClientSecret " +
                    "(e.g. via .NET user-secrets from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(PayPalHttpClientName);
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = payPalSettings.ClientId,
                    ClientSecret = payPalSettings.ClientSecret
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(15) // per attempt
                }
            };

            // Optional verbatim base-URL override; covers every call including the OAuth token request.
            if (!string.IsNullOrEmpty(payPalSettings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = payPalSettings.BaseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPaymentGateway, PayPalPaymentGateway>();
    }
}
