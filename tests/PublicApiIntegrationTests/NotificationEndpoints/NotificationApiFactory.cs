using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// A test host that swaps the real Twilio-backed <see cref="ISmsProvider"/> for a
/// <see cref="FakeSmsProvider"/>, so notification behaviour is verified without sending real messages.
/// Each instance has its own isolated in-memory store.
/// </summary>
public class NotificationApiFactory : WebApplicationFactory<Program>
{
    public FakeSmsProvider Sms { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISmsProvider));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }
            services.AddSingleton<ISmsProvider>(Sms);
        });
    }
}
