using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Boots the PublicApi with the real pipeline but PayPal replaced by <see cref="FakePayPalPaymentService"/>,
/// so the functional tests exercise routing, auth, ownership and idempotency without any network calls.
/// </summary>
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPayPalPaymentService));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }
            // Singleton so the fake's remembered authorization amounts persist across requests, the way
            // PayPal's state does (a hold placed in one request is captured in a later one).
            services.AddSingleton<IPayPalPaymentService, FakePayPalPaymentService>();
        });
    }
}
