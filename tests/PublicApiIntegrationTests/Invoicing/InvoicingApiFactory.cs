using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.Invoicing;

/// <summary>
/// A test host that swaps the real Visa provider for a deterministic in-memory fake, so the invoicing
/// endpoints can be driven end-to-end without contacting the sandbox. The fake is a singleton so its
/// state survives across requests within a single factory instance.
/// </summary>
public class InvoicingApiFactory : WebApplicationFactory<Program>
{
    public FakeInvoiceProvider Provider { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(IInvoiceProvider)).ToList();
            foreach (var descriptor in existing)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IInvoiceProvider>(Provider);
        });
    }
}
