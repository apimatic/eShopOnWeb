using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.InvoiceEndpoints;

/// <summary>
/// A test host that swaps the Visa-backed <see cref="IInvoiceService"/> for a fake, so the invoice
/// endpoints are tested through the real routing/auth pipeline without reaching the provider.
/// </summary>
public sealed class InvoiceApiFactory : WebApplicationFactory<Program>
{
    public FakeInvoiceService Fake { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IInvoiceService));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IInvoiceService>(Fake);
        });
    }
}
