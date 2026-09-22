using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Boots the PublicApi host (with the startup PayPal fail-fast check active, satisfied by the placeholder
/// PayPal config in appsettings.test.json) but replaces the live PayPal gateway with an in-memory fake.
/// </summary>
public class FakePaymentApiFactory : WebApplicationFactory<Program>
{
    public FakePayPalGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPayPalPaymentGateway));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddSingleton<IPayPalPaymentGateway>(Gateway);
        });
    }

    public HttpClient CreateClientFor(string bearerToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    public HttpClient CreateAdminClient() => CreateClientFor(ApiTokenHelper.GetAdminUserToken());
    public HttpClient CreateShopperClient() => CreateClientFor(ApiTokenHelper.GetNormalUserToken());
}
