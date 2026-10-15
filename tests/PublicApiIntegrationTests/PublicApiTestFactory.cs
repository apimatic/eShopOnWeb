using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using PublicApiIntegrationTests.SubscriptionEndpoints;

namespace PublicApiIntegrationTests;

/// <summary>
/// Hosts PublicApi on the in-memory database with placeholder Maxio settings, and routes the Maxio SDK's
/// HttpClient to <see cref="FakeMaxio"/> — no secrets and no network needed.
/// </summary>
public class PublicApiTestFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new()
    {
        ["UseOnlyInMemoryDatabase"] = "true",
        ["Maxio:ApiKey"] = "test-api-key",
        ["Maxio:Subdomain"] = "test-site",
        ["Maxio:ProductFamilyHandle"] = FakeMaxio.FamilyHandle,
        ["Maxio:BaseUrl"] = "",
        ["Maxio:Environment"] = ""
    };

    public PublicApiTestFactory(IDictionary<string, string?>? overrides = null)
    {
        foreach (var setting in overrides ?? new Dictionary<string, string?>())
        {
            _settings[setting.Key] = setting.Value;
        }
    }

    public FakeMaxio Maxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: user-secrets (real Maxio credentials on a developer machine) must not load in tests.
        builder.UseEnvironment("Testing");
        foreach (var setting in _settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureTestServices(services =>
            services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Maxio));
    }

    /// <summary>The in-memory database is shared by every host in the test run: start each test without enrollments.</summary>
    public async Task ResetEnrollmentsAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        db.SubscriptionEnrollments.RemoveRange(db.SubscriptionEnrollments.ToList());
        await db.SaveChangesAsync();
    }
}
