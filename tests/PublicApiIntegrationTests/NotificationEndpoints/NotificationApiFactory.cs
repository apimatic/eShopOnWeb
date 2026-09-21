using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// Boots the PublicApi with the live Twilio gateway replaced by an in-memory <see cref="FakeSmsGateway"/>,
/// and its catalog/order/notification store isolated to this factory (a unique in-memory database), so each
/// test runs against a clean, private dataset — the EF in-memory provider otherwise shares one named store
/// process-wide.
/// </summary>
public sealed class NotificationApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "Catalog-" + Guid.NewGuid().ToString("N");

    public FakeSmsGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmsGateway>();
            services.AddSingleton<ISmsGateway>(Gateway);

            // Re-point CatalogContext at a per-factory in-memory database for test isolation.
            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.AddDbContext<CatalogContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
