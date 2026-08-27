using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class Dependencies
{
    /// <summary>
    /// Binds the PayPal: configuration section and registers the payment gateway and
    /// payment/saved-card services. No PayPal setting value is hard-coded.
    /// </summary>
    public static void ConfigurePaymentServices(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<PayPalSettings>()
            .Bind(configuration.GetSection(PayPalSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId), "PayPal:ClientId is required (set it from PAYPAL_CLIENT_ID).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.ClientSecret), "PayPal:ClientSecret is required (set it from PAYPAL_CLIENT_SECRET).")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Currency), "PayPal:Currency is required (set it from PAYPAL_CURRENCY).")
            .ValidateOnStart();

        services.AddHttpClient<IPaymentGateway, PayPalPaymentGateway>();
        services.AddSingleton<IPaymentCurrencyProvider, PaymentCurrencyProvider>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ISavedCardService, SavedCardService>();
        services.AddScoped<IOrderService, OrderService>();
    }

    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        bool useOnlyInMemoryDatabase = false;
        if (configuration["UseOnlyInMemoryDatabase"] != null)
        {
            useOnlyInMemoryDatabase = bool.Parse(configuration["UseOnlyInMemoryDatabase"]!);
        }

        if (useOnlyInMemoryDatabase)
        {
            services.AddDbContext<CatalogContext>(c =>
               c.UseInMemoryDatabase("Catalog"));
         
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseInMemoryDatabase("Identity"));
        }
        else
        {
            // use real database
            // Requires LocalDB which can be installed with SQL Server Express 2016
            // https://www.microsoft.com/en-us/download/details.aspx?id=54284
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));

            // Add Identity DbContext
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }
    }
}
