using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PublicApiIntegrationTests.Fakes;

namespace PublicApiIntegrationTests;

// Boots the real PublicApi app (real routing, auth, EF-InMemory persistence, domain/service layer)
// but swaps the real PayPal gateway for a deterministic fake, so the payment endpoints can be
// exercised over real HTTP without a network dependency in the automated test suite.
public class PaymentsApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        });
    }

    public HttpClient NewClient() => CreateClient();
}
