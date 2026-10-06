using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class Dependencies
{
    public const string MaxioHttpClientName = "Maxio";

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

        ConfigureServicesMaxio(configuration, services);
    }

    /// <summary>
    /// Registers the Maxio Advanced Billing client and integration service. Registration is
    /// intentionally lazy: the SDK client is only constructed when first resolved (i.e. when
    /// a billing endpoint is actually hit), so hosts without Maxio configuration start fine
    /// and the integration fails closed with a clear "not configured" error at first use.
    /// </summary>
    private static void ConfigureServicesMaxio(IConfiguration configuration, IServiceCollection services)
    {
        var maxioSettings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>()
            ?? new MaxioSettings();
        services.AddSingleton(maxioSettings);

        services.AddMemoryCache();

        services.AddHttpClient(MaxioHttpClientName, client =>
            {
                // Bounds one attempt; the integration service also applies a total call budget.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<MaxioSettings>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(MaxioHttpClientName);

            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    // Maxio Advanced Billing is HTTP Basic where the username is the API key
                    // and the password is the literal "x".
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Environment = ServerEnvironment.Us
            };
            options.Server.Production.Us.Site = settings.Subdomain;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<IMaxioBillingService, MaxioBillingService>();
    }
}
