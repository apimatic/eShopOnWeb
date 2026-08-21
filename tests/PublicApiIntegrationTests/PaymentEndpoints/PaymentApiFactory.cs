using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>A PublicApi test host with the PayPal gateway replaced by an in-memory fake.</summary>
public class PaymentApiFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    public FakePaymentGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPaymentGateway));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }
            services.AddScoped<IPaymentGateway>(_ => Gateway);
        });
    }
}
