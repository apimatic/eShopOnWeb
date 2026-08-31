using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Wires up the Visa / CyberSource invoicing capability: binds <see cref="VisaSettings"/> from the
/// "Visa" configuration section (falling back to the documented environment variable names so the
/// same build can target a different account/address), registers a named HttpClient whose pipeline
/// forces every provider call onto the configured base address, and registers the provider gateway,
/// the invoicing application service and the order service the API endpoints depend on.
/// </summary>
public static class VisaInvoicingRegistration
{
    public const string HttpClientName = "Visa.Invoicing";

    public static IServiceCollection AddVisaInvoicing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<VisaSettings>(configuration.GetSection(VisaSettings.SectionName));

        // Fall back to the documented environment variable NAMES when a value is not otherwise
        // configured. Only names are referenced here — never any value.
        services.PostConfigure<VisaSettings>(settings =>
        {
            settings.BaseUrl = FirstNonEmpty(settings.BaseUrl, Environment.GetEnvironmentVariable("VISA_BASE_URL"));
            settings.MerchantId = FirstNonEmpty(settings.MerchantId, Environment.GetEnvironmentVariable("VISA_MERCHANT_ID"));
            settings.KeyId = FirstNonEmpty(settings.KeyId, Environment.GetEnvironmentVariable("VISA_KEY_ID"));
            settings.SecretKey = FirstNonEmpty(settings.SecretKey, Environment.GetEnvironmentVariable("VISA_SECRET_KEY"));
        });

        services.AddHttpClient(HttpClientName)
            .AddHttpMessageHandler(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<VisaSettings>>().Value;
                if (string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    throw new InvalidOperationException(
                        "Visa:BaseUrl is not configured. Every provider call is routed through it, so it must be set.");
                }
                return new VisaBaseAddressHandler(new Uri(settings.BaseUrl, UriKind.Absolute));
            });

        services.AddScoped<IInvoiceProvider, CyberSourceInvoiceProvider>();
        services.AddScoped<IInvoiceService, InvoiceService>();

        // The public API places orders directly from catalog items, so it needs the order service.
        services.AddScoped<IOrderService, OrderService>();

        return services;
    }

    private static string FirstNonEmpty(string current, string? fallback) =>
        !string.IsNullOrWhiteSpace(current) ? current : (fallback ?? string.Empty);
}
