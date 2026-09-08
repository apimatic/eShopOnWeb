using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.SubscriptionEndpoints;

public class SubscriptionTestApplication : WebApplicationFactory<AuthenticateEndpoint>
{
    private readonly string _environment = "Testing";

    /// <summary>
    /// Substitute for the Maxio-backed billing service; tests configure behaviour per scenario so no
    /// live Maxio credentials or network access are required.
    /// </summary>
    public ISubscriptionBillingService BillingService { get; } = Substitute.For<ISubscriptionBillingService>();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureServices(services =>
        {
            services.AddScoped(sp =>
            {
                return new DbContextOptionsBuilder<CatalogContext>()
                    .UseInMemoryDatabase("DbForSubscriptionApi")
                    .UseApplicationServiceProvider(sp)
                    .Options;
            });
            services.AddScoped(sp =>
            {
                return new DbContextOptionsBuilder<AppIdentityDbContext>()
                    .UseInMemoryDatabase("IdentityDbForSubscriptionApi")
                    .UseApplicationServiceProvider(sp)
                    .Options;
            });

            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISubscriptionBillingService));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }
            services.AddSingleton(BillingService);
        });

        return base.CreateHost(builder);
    }
}
