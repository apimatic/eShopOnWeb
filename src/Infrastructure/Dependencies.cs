using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

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

        // Configure Maxio Advanced Billing
        var maxioSettings = configuration.GetSection("Maxio").Get<MaxioSettings>();
        if (maxioSettings?.ApiKey != null)
        {
            services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));
            services.AddMaxioAdvancedBillingClient(options =>
            {
                options.BasicAuth = new BasicAuthCredentials
                {
                    Username = maxioSettings.ApiKey,
                    Password = "x"
                };
                options.Environment = ServerEnvironment.Us;

                if (!string.IsNullOrEmpty(maxioSettings.BaseUrl))
                {
                    options.Server.Production.Us.BaseUrl = maxioSettings.BaseUrl;
                }
                else if (!string.IsNullOrEmpty(maxioSettings.Subdomain))
                {
                    options.Server.Production.Us.Site = maxioSettings.Subdomain;
                }
            });
        }
    }
}
