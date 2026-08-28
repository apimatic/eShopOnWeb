using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.NotificationEndpoints;

internal sealed class NotificationApiFactory : WebApplicationFactory<Program>
{
    public FakeTwilioClient Twilio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("UseOnlyInMemoryDatabase", "true");
        builder.UseSetting("InMemoryDatabaseName", GetHashCode().ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITwilioLookupClient>();
            services.RemoveAll<ITwilioMessagingClient>();
            services.AddSingleton<ITwilioLookupClient>(Twilio);
            services.AddSingleton<ITwilioMessagingClient>(Twilio);
        });
    }
}
