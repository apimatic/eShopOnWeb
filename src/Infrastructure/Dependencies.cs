using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

        services.Configure<PayPalSettings>(configuration.GetSection("PayPal"));

        const string PayPalClientName = "PayPalSdk";
        services.AddHttpClient(PayPalClientName, c =>
            {
                c.Timeout = System.TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = System.TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient(PayPalClientName);
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;

            var options = new PayPalServerSdkClientOptions
            {
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                },
                Environment = ServerEnvironment.Sandbox,
                Retry = RetryOptions.Default() with { Timeout = System.TimeSpan.FromSeconds(20), MaxRetries = 2 }
            };

            if (!string.IsNullOrEmpty(settings.BaseUrl))
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl;
            else if (string.Equals(settings.Environment, "live", System.StringComparison.OrdinalIgnoreCase))
                options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com";

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalService, PayPalService>();

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
}
