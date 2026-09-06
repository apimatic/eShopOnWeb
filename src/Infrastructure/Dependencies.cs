using Microsoft.EntityFrameworkCore;
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

        ConfigureMaxioServices(configuration, services);
    }

    private static void ConfigureMaxioServices(IConfiguration configuration, IServiceCollection services)
    {
        var maxioSettings = new MaxioSettings();
        var maxioConfig = configuration.GetSection("Maxio");
        maxioSettings.ApiKey = maxioConfig["ApiKey"] ?? string.Empty;
        maxioSettings.Subdomain = maxioConfig["Subdomain"] ?? string.Empty;
        maxioSettings.ProductFamilyHandle = maxioConfig["ProductFamilyHandle"] ?? string.Empty;
        maxioSettings.BaseUrl = maxioConfig["BaseUrl"];

        if (!string.IsNullOrEmpty(maxioSettings.ApiKey) && !string.IsNullOrEmpty(maxioSettings.Subdomain))
        {
            services.AddSingleton(maxioSettings);
            services.AddHttpClient<IMaxioClient, MaxioClient>();
            services.AddScoped<ISubscriptionService, SubscriptionService>();
        }
    }
}
