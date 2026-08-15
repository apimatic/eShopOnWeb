using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Boots the PublicApi with the PayPal gateways replaced by in-memory fakes, so the payment endpoints
/// can be exercised end-to-end (routing, auth, persistence) without contacting PayPal.
/// </summary>
public class PaymentApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPayPalPaymentGateway>();
            services.RemoveAll<IPayPalVaultGateway>();
            services.RemoveAll<IPayPalReportingGateway>();

            services.AddSingleton<IPayPalPaymentGateway, FakePaymentGateway>();
            services.AddSingleton<IPayPalVaultGateway, FakeVaultGateway>();
            services.AddSingleton<IPayPalReportingGateway, FakeReportingGateway>();
        });
    }
}
