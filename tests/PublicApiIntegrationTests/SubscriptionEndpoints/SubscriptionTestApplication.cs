using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Web application factory that swaps the real Maxio client for a fake so the
/// subscription endpoints can be tested without network access.
/// </summary>
public class SubscriptionTestApplication : WebApplicationFactory<Program>
{
    public FakeMaxioClient FakeMaxioClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMaxioClient>();
            services.AddSingleton<IMaxioClient>(FakeMaxioClient);
        });
    }
}
