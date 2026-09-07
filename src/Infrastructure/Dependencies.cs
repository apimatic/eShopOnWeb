using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        // Configure Maxio - load from environment variables with fallback to config
        var maxioConfig = new MaxioConfiguration
        {
            ApiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY") ?? configuration["Maxio:ApiKey"] ?? "",
            Subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN") ?? configuration["Maxio:Subdomain"] ?? "",
            ProductFamilyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY") ?? configuration["Maxio:ProductFamilyHandle"] ?? "",
            BaseUrl = Environment.GetEnvironmentVariable("MAXIO_BASE_URL") ?? configuration["Maxio:BaseUrl"]
        };
        services.AddSingleton(maxioConfig);
        services.AddHttpClient<IMaxioSubscriptionService, MaxioSubscriptionService>();
    }
}
