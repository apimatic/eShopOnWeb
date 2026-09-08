using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests;

public static class SubscriptionTestClientFactory
{
    public static HttpClient CreateClient(IMaxioSubscriptionService service)
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(service);
                });
            });

        return factory.CreateClient();
    }
}
