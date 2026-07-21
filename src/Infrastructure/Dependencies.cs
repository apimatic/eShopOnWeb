using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    }

    /// <summary>
    /// Registers the Maxio Advanced Billing integration — the SDK client (target server resolved
    /// from <see cref="MaxioSettings"/>, §2.3), the single <see cref="IBillingClient"/> seam, the
    /// <see cref="ISubscriptionService"/> use-case surface, and the startup component validation.
    /// Called from both the Web and PublicApi composition roots so the provider is still touched
    /// in exactly one Infrastructure class.
    /// </summary>
    public static void AddMaxioBillingClient(IConfiguration configuration, IServiceCollection services)
    {
        var maxioSection = configuration.GetSection("Maxio");
        services.Configure<MaxioSettings>(maxioSection);
        var maxioSettings = maxioSection.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(maxioSettings.ApplyTo);

        services.AddScoped<IBillingClient, MaxioBillingClient>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddHostedService<MaxioComponentValidationHostedService>();
    }
}
