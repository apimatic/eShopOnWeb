using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Payments;

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

        ConfigurePayPal(configuration, services);
    }

    private static void ConfigurePayPal(IConfiguration configuration, IServiceCollection services)
    {
        var settings = configuration.GetSection(PayPalOptions.SectionName).Get<PayPalOptions>()
            ?? new PayPalOptions();
        services.AddSingleton(settings);
        services.AddHttpClient("PayPal", client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });
        services.AddSingleton(provider =>
        {
            if (string.IsNullOrWhiteSpace(settings.ClientId)
                || string.IsNullOrWhiteSpace(settings.ClientSecret)
                || string.IsNullOrWhiteSpace(settings.Environment)
                || string.IsNullOrWhiteSpace(settings.Currency))
                throw new InvalidOperationException(
                    "PayPal:ClientId, PayPal:ClientSecret, PayPal:Environment and PayPal:Currency must be configured.");
            if (!string.Equals(settings.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This SDK build supports PayPal:Environment=Sandbox only.");

            var options = new PayPalServerSdk.PayPalServerSdkClientOptions
            {
                Environment = PayPalServerSdk.Servers.ServerEnvironment.Sandbox,
                Oauth2 = new PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret,
                    Scope = null
                },
                Retry = PayPalServerSdk.Core.Configuration.RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;

            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("PayPal");
            return new PayPalServerSdk.PayPalServerSdkClient(client, options);
        });
        services.AddSingleton<IPayPalGateway, PayPalGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
    }
}
