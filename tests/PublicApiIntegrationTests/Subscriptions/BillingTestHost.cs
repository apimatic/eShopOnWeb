using System.Collections.Generic;
using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace PublicApiIntegrationTests.Subscriptions;

internal sealed class BillingTestHost : WebApplicationFactory<Program>
{
    public MaxioStub Provider { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UseOnlyInMemoryDatabase"] = "true",
            ["Maxio:ApiKey"] = "test-only-placeholder",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = "test-family",
            ["Maxio:BaseUrl"] = "https://billing.invalid/custom-base"
        }));
        builder.ConfigureServices(services =>
        {
            var database = "BillingTests-" + Guid.NewGuid().ToString("N");
            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(database + "Catalog"));
            services.AddDbContext<AppIdentityDbContext>(o => o.UseInMemoryDatabase(database + "Identity"));
            // Tests invoke the sweep directly to avoid timers and to assert persistent recovery state.
            var worker = services.SingleOrDefault(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(BillingReconciliationWorker));
            if (worker != null) services.Remove(worker);
            services.AddHttpClient("MaxioBilling").ConfigurePrimaryHttpMessageHandler(() => Provider);
        });
    }
}
