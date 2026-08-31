using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

public static class InvoicingServiceCollectionExtensions
{
    /// <summary>
    /// Wires up the Visa/CyberSource invoicing integration: binds the <c>Visa</c> options from
    /// configuration (base address and credentials), the billing currency, the provider gateway, and
    /// the application services that orchestrate the invoicing use cases.
    /// </summary>
    public static IServiceCollection AddVisaInvoicing(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new VisaInvoicingOptions
        {
            BaseUrl = configuration["Visa:BaseUrl"] ?? string.Empty,
            MerchantId = configuration["Visa:MerchantId"] ?? string.Empty,
            KeyId = configuration["Visa:KeyId"] ?? string.Empty,
            SecretKey = configuration["Visa:SecretKey"] ?? string.Empty
        };
        services.AddSingleton(options);

        var invoicingSettings = new InvoicingSettings
        {
            Currency = configuration["Visa:Currency"] ?? "USD"
        };
        services.AddSingleton(invoicingSettings);

        services.AddScoped<IInvoiceGateway, CyberSourceInvoiceGateway>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();

        return services;
    }
}
