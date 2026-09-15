using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.Notifications;

/// <summary>
/// Boots the real PublicApi host but swaps the Twilio-backed <see cref="ISmsGateway"/> for an
/// inspectable in-memory fake, so the HTTP surface (routing, auth, JSON, id fields, lifecycle)
/// can be exercised without touching the real provider.
/// </summary>
public sealed class SmsApiFactory : WebApplicationFactory<Program>
{
    public FakeSmsGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var existing = services.SingleOrDefault(d => d.ServiceType == typeof(ISmsGateway));
            if (existing is not null)
            {
                services.Remove(existing);
            }
            services.AddSingleton<ISmsGateway>(Gateway);
        });
    }
}
