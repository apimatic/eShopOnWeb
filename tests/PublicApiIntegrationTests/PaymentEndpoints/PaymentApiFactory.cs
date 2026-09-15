using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Boots the real PublicApi (in-memory DB, real endpoints, services, auth and EF) but replaces the PayPal
/// gateway with an in-memory fake, so the payment flows are driven end to end through the HTTP surface without
/// any network call. Each factory gets its own uniquely-named in-memory store so tests are fully isolated
/// (the EF in-memory provider otherwise shares one store per database name across the whole process).
/// </summary>
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    public FakePayPalGateway Gateway { get; } = new();

    private readonly string _catalogDb = "Catalog-" + Guid.NewGuid();
    private readonly string _identityDb = "Identity-" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var gatewayDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPayPalPaymentGateway));
            if (gatewayDescriptor is not null)
                services.Remove(gatewayDescriptor);
            services.AddSingleton<IPayPalPaymentGateway>(Gateway);

            services.RemoveAll<DbContextOptions<CatalogContext>>();
            services.RemoveAll<CatalogContext>();
            services.AddDbContext<CatalogContext>(o => o.UseInMemoryDatabase(_catalogDb));

            services.RemoveAll<DbContextOptions<AppIdentityDbContext>>();
            services.RemoveAll<AppIdentityDbContext>();
            services.AddDbContext<AppIdentityDbContext>(o => o.UseInMemoryDatabase(_identityDb));
        });
    }
}
