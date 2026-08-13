using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// A test host with the real Twilio gateway replaced by an in-memory <see cref="FakeSmsGateway"/>,
/// so the full endpoint/service stack is exercised over HTTP without sending real messages.
/// </summary>
public class NotificationApiFactory : WebApplicationFactory<Program>
{
    public FakeSmsGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmsGateway>();
            services.AddSingleton<ISmsGateway>(Gateway);
        });
    }
}
