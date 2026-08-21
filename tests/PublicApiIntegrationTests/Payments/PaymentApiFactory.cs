using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.Payments;

/// <summary>
/// Test host that runs PublicApi against the in-memory database and swaps the real PayPal gateway
/// for <see cref="FakePaymentGateway"/>, so the HTTP surface (auth, scoping, idempotency, response
/// shapes) is verified deterministically without touching PayPal.
/// </summary>
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    public FakePaymentGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true",
                // Test-only placeholder credentials (never the real values) so client construction
                // never guard-fails even though the fake gateway means it is never resolved.
                ["PayPal:ClientId"] = "test-client-id",
                ["PayPal:ClientSecret"] = "test-client-secret",
                ["PayPal:Environment"] = "sandbox",
                ["PayPal:Currency"] = "USD"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPaymentGateway));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton<IPaymentGateway>(Gateway);
        });
    }
}
