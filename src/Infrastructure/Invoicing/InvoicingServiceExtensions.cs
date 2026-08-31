using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Invoicing.Visa;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

public static class InvoicingServiceExtensions
{
    /// <summary>
    /// Registers the Visa (CyberSource) invoicing integration: the bound <see cref="VisaSettings"/>,
    /// the base-URL-rewriting HttpClient every provider call flows through, the provider itself,
    /// and the application invoicing service.
    /// </summary>
    public static IServiceCollection AddVisaInvoicing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<VisaSettings>(configuration.GetSection(VisaSettings.SectionName));

        services.AddTransient<VisaBaseUrlHandler>();
        services.AddHttpClient("cybersource-invoicing")
            .AddHttpMessageHandler<VisaBaseUrlHandler>();

        services.AddScoped<IInvoiceProvider, CyberSourceInvoiceProvider>();
        services.AddScoped<IInvoiceService, InvoiceService>();

        return services;
    }
}
