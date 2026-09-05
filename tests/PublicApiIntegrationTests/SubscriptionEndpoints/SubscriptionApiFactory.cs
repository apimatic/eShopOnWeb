using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// A standalone <see cref="Program"/> host, separate from <see cref="ProgramTest"/>'s shared
/// instance, with the real Maxio gateway swapped out for <see cref="FakeMaxioSubscriptionGateway"/>
/// so these tests don't need network access or real Maxio sandbox credentials.
/// </summary>
public class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    public FakeMaxioSubscriptionGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMaxioSubscriptionGateway>();
            services.AddSingleton<IMaxioSubscriptionGateway>(Gateway);
        });
    }
}
