using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
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
    }

    /// <summary>
    /// Registers the single Maxio Advanced Billing seam (§2.2/§4.3) — <see cref="MaxioSettings"/>
    /// bound from the "Maxio" configuration section, the generated SDK client, and
    /// <see cref="IBillingClient"/>/<see cref="MaxioBillingClient"/>. Called identically from both
    /// hosts (Web and PublicApi) since neither shares the other's DI composition root.
    /// </summary>
    public static void AddMaxioBillingServices(IConfiguration configuration, IServiceCollection services)
    {
        var maxioSection = configuration.GetSection("Maxio");
        services.Configure<MaxioSettings>(maxioSection);
        var maxioSettings = maxioSection.Get<MaxioSettings>() ?? new MaxioSettings();

        services.AddMaxioAdvancedBillingClient(options =>
        {
            options.BasicAuth = new BasicAuthCredentials { Username = maxioSettings.ApiKey, Password = "x" };

            // Explicit Maxio:BaseUrl wins verbatim over the Subdomain-derived host (§2.3) — the
            // same build can target production, a dev/sandbox tenant, or a local mock server
            // purely through configuration.
            if (maxioSettings.IsEuEnvironment)
            {
                options.Environment = ServerEnvironment.Eu;
                options.Server.Production.Eu.Site = maxioSettings.Subdomain;
                options.Server.Production.Eu.BaseUrl = maxioSettings.ResolveBaseUrl();
            }
            else
            {
                options.Environment = ServerEnvironment.Us;
                options.Server.Production.Us.Site = maxioSettings.Subdomain;
                options.Server.Production.Us.BaseUrl = maxioSettings.ResolveBaseUrl();
            }
        });

        services.AddScoped<IBillingClient, MaxioBillingClient>();
    }
}
