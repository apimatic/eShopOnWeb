using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

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

        ConfigureMaxioClient(configuration, services);
    }

    private static void ConfigureMaxioClient(IConfiguration configuration, IServiceCollection services)
    {
        var apiKey = configuration["Maxio:ApiKey"];
        var subdomain = configuration["Maxio:Subdomain"];

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(subdomain))
        {
            return;
        }

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey,
                Password = "x"
            };
            options.Environment = ServerEnvironment.Us;

            var baseUrlOverride = configuration["Maxio:BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrlOverride))
            {
                options.Server.Production.Us.BaseUrl = baseUrlOverride;
            }
            else
            {
                options.Server.Production.Us.Site = subdomain;
            }
        });
    }
}
