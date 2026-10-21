using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// PublicApi with its Maxio HttpClient wired to <see cref="FakeMaxio"/> and its own databases, so each test
/// starts from an empty billing state and no request can leave the process.
/// </summary>
public sealed class MaxioTestApplication : WebApplicationFactory<Program>
{
    private readonly string _databaseSuffix = Guid.NewGuid().ToString("N");
    private readonly Action<MaxioOptions>? _configureMaxio;

    public MaxioTestApplication(Action<MaxioOptions>? configureMaxio = null)
    {
        _configureMaxio = configureMaxio;
    }

    public FakeMaxio Maxio { get; } = new();

    /// <summary>The clock the billing service reads (claim leases); tests move it forward.</summary>
    public AdjustableClock Clock { get; } = new();

    public HttpClient CreateClientFor(string bearerToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<MaxioOptions>(options =>
            {
                // Whatever the machine's configuration says, never point a test at a real site.
                options.BaseUrl = "https://maxio.test.invalid";
                _configureMaxio?.Invoke(options);
            });

            services.AddSingleton<TimeProvider>(Clock);

            services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Maxio);

            services.AddScoped(sp => new DbContextOptionsBuilder<CatalogContext>()
                .UseInMemoryDatabase("SubscriptionTestsCatalog-" + _databaseSuffix)
                .UseApplicationServiceProvider(sp)
                .Options);
            services.AddScoped(sp => new DbContextOptionsBuilder<AppIdentityDbContext>()
                .UseInMemoryDatabase("SubscriptionTestsIdentity-" + _databaseSuffix)
                .UseApplicationServiceProvider(sp)
                .Options);
        });
    }
}

/// <summary>System time plus an offset the test controls.</summary>
public sealed class AdjustableClock : TimeProvider
{
    private TimeSpan _offset = TimeSpan.Zero;

    public void Advance(TimeSpan by) => _offset += by;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;
}
