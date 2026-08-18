using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.Payments;

/// <summary>
/// Boots the real PublicApi host but swaps the PayPal gateway for a fake and supplies test PayPal
/// configuration, so the payment endpoints and services run for real over the in-memory database.
/// </summary>
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    public FakePayPalPaymentService PayPal { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseOnlyInMemoryDatabase"] = "true",
                ["PayPal:ClientId"] = "test-client-id",
                ["PayPal:ClientSecret"] = "test-client-secret",
                ["PayPal:Environment"] = "sandbox",
                ["PayPal:Currency"] = "USD"
            });
        });

        builder.ConfigureServices(services =>
        {
            var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IPayPalPaymentService));
            if (existing is not null)
                services.Remove(existing);

            services.AddSingleton<IPayPalPaymentService>(PayPal);
        });
    }
}
