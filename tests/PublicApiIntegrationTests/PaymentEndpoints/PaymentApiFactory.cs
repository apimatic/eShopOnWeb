using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// A test host that replaces the real PayPal gateway with an in-memory fake, so the payment flows
/// are driven end-to-end through the API without touching PayPal. Each factory instance owns an
/// isolated in-memory database and a single fake gateway (so state persists across requests).
/// </summary>
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    public FakePaymentGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPaymentGateway));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<IPaymentGateway>(Gateway);
        });
    }
}
