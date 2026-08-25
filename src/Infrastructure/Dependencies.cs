using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.PayPal;
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

        ConfigurePayPal(configuration, services);
    }

    private static void ConfigurePayPal(IConfiguration configuration, IServiceCollection services)
    {
        var settings = new PayPalSettings();
        configuration.GetSection("PayPal").Bind(settings);
        services.AddSingleton(settings);

        const string httpClientName = "PayPalSdk";

        services.AddHttpClient(httpClientName, c =>
        {
            c.Timeout = System.TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            PooledConnectionLifetime = System.TimeSpan.FromMinutes(5)
        });

        services.AddSingleton(sp =>
        {
            var s = sp.GetRequiredService<PayPalSettings>();
            var factory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            var httpClient = factory.CreateClient(httpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = s.ClientId,
                    ClientSecret = s.ClientSecret
                },
                Environment = ServerEnvironment.Sandbox
            };

            if (!string.IsNullOrWhiteSpace(s.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = s.BaseUrl;

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalService, PayPalService>();
    }
}
