using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            // use real database
            // Requires LocalDB which can be installed with SQL Server Express 2016
            // https://www.microsoft.com/en-us/download/details.aspx?id=54284
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

            // Add Identity DbContext
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }

        ConfigurePayPalServices(configuration, services);
    }

    private static void ConfigurePayPalServices(IConfiguration configuration, IServiceCollection services)
    {
        var settings = new PayPalSettings
        {
            ClientId = configuration["PayPal:ClientId"] ?? string.Empty,
            ClientSecret = configuration["PayPal:ClientSecret"] ?? string.Empty,
            Environment = configuration["PayPal:Environment"] ?? "sandbox",
            Currency = configuration["PayPal:Currency"] ?? "USD",
            BaseUrl = configuration["PayPal:BaseUrl"]
        };

        services.AddSingleton(settings);

        services.AddPayPalServerSdkClient(o =>
        {
            o.Environment = ServerEnvironment.Sandbox;
            o.Oauth2 = new OAuth2ClientCredentials
            {
                ClientId = settings.ClientId,
                ClientSecret = settings.ClientSecret
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                o.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
        });

        services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();
    }
}
