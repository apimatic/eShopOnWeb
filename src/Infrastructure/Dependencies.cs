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
            // use real database
            // Requires LocalDB which can be installed with SQL Server Express 2016
            // https://www.microsoft.com/en-us/download/details.aspx?id=54284
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

            // Add Identity DbContext
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }
    }

    /// <summary>
    /// Binds the PayPal: configuration section and registers a single long-lived
    /// PayPal SDK client over a named, factory-managed HttpClient.
    /// </summary>
    public static void ConfigurePayPalServices(IConfiguration configuration, IServiceCollection services)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.CONFIG_NAME));
        var settings = configuration.GetSection(PayPalSettings.CONFIG_NAME).Get<PayPalSettings>() ?? new PayPalSettings();

        services.AddHttpClient(PayPalPaymentGateway.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(PayPalPaymentGateway.HttpClientName);
            var options = new PayPalServerSdkClientOptions
            {
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId ?? string.Empty,
                    ClientSecret = settings.ClientSecret ?? string.Empty
                },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(30) }
            };

            // The SDK ships only a Sandbox environment; production is reached via the
            // base-URL override, which also covers the OAuth token request.
            var baseUrl = settings.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl) &&
                (string.Equals(settings.Environment, "production", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(settings.Environment, "live", StringComparison.OrdinalIgnoreCase)))
            {
                baseUrl = "https://api-m.paypal.com";
            }
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = baseUrl;
            }

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();
    }
}
