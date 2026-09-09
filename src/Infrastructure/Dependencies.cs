using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
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
            // https://www.microsoft.com/en-us/download/details.aspx?id=54269
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

            // Add Identity DbContext
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }

        // Maxio Advanced Billing subscription integration
        ApplyMaxioEnvironmentVariableFallbacks(configuration);
        services.Configure<MaxioOptions>(configuration.GetSection(MaxioOptions.SectionName));
        services.AddHttpClient<IMaxioClient, MaxioClient>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
    }

    /// <summary>
    /// Lets the documented MAXIO_* environment variables supply the Maxio configuration
    /// section when the corresponding keys were not provided by user-secrets or settings.
    /// </summary>
    private static void ApplyMaxioEnvironmentVariableFallbacks(IConfiguration configuration)
    {
        if (configuration is not IConfigurationRoot configRoot)
            return;

        SetIfMissing(configRoot, "Maxio:ApiKey", "MAXIO_API_KEY");
        SetIfMissing(configRoot, "Maxio:Subdomain", "MAXIO_SITE_SUBDOMAIN");
        SetIfMissing(configRoot, "Maxio:ProductFamilyHandle", "MAXIO_DEFAULT_PRODUCT_FAMILY");
        SetIfMissing(configRoot, "Maxio:BaseUrl", "MAXIO_BASE_URL");
    }

    private static void SetIfMissing(IConfigurationRoot configRoot, string configKey, string environmentVariable)
    {
        if (!string.IsNullOrEmpty(configRoot[configKey]))
            return;
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrEmpty(value))
            configRoot[configKey] = value;
    }
}
