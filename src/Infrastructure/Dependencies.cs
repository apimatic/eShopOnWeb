using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
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

        // Configure Maxio - read from appsettings and environment
        var maxioApiKey = configuration["Maxio:ApiKey"] ?? configuration["MAXIO_API_KEY"] ?? string.Empty;
        var maxioSubdomain = configuration["Maxio:Subdomain"] ?? configuration["MAXIO_SITE_SUBDOMAIN"] ?? string.Empty;
        var maxioFamily = configuration["Maxio:ProductFamilyHandle"] ?? configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"] ?? string.Empty;
        var maxioBaseUrl = configuration["Maxio:BaseUrl"] ?? configuration["MAXIO_BASE_URL"];

        services.Configure<MaxioConfiguration>(opts =>
        {
            opts.ApiKey = maxioApiKey;
            opts.Subdomain = maxioSubdomain;
            opts.ProductFamilyHandle = maxioFamily;
            opts.BaseUrl = maxioBaseUrl;
        });
        services.AddScoped<HttpClient>();
        services.AddScoped<IMaxioApiClient, MaxioApiClient>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
    }
}
