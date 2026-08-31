using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Wires up the Visa (CyberSource) invoicing capability: the provider client, the orchestration
/// service, and order placement. <c>Visa:BaseUrl</c> is bound from configuration; the credentials are
/// bound from the same section, which is expected to be populated from user-secrets.
/// </summary>
public static class InvoicingServiceCollectionExtensions
{
    public static IServiceCollection AddVisaInvoicing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<VisaSettings>(configuration.GetSection(VisaSettings.CONFIG_NAME));

        services.AddScoped<IInvoiceProvider, VisaInvoiceProvider>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();

        return services;
    }
}
