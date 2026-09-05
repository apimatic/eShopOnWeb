using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.EntityFrameworkCore;
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

        var maxioSection = configuration.GetSection("Maxio");
        services.Configure<MaxioSettings>(maxioSection);
        var maxioSettings = maxioSection.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(o =>
        {
            o.BasicAuth = new BasicAuthCredentials
            {
                Username = maxioSettings.ApiKey,
                Password = "x"
            };
            o.Server.Production.Us.Site = maxioSettings.Subdomain;
            if (!string.IsNullOrWhiteSpace(maxioSettings.BaseUrl))
            {
                o.Server.Production.Us.BaseUrl = maxioSettings.BaseUrl;
            }
        });
        services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();
    }
}
