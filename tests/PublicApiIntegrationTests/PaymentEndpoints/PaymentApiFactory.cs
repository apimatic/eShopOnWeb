using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// A test host that swaps the real PayPal gateway for <see cref="FakePayPalGateway"/> so the payment
/// endpoints and services run end to end (real DI, in-memory DB) without network calls.
/// </summary>
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    public FakePayPalGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPayPalGateway>();
            services.AddSingleton<IPayPalGateway>(Gateway);
        });
    }
}
