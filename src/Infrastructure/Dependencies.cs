using System;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure;

public static class Dependencies
{
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

        ConfigurePaymentServices(configuration, services);
    }

    private static void ConfigurePaymentServices(IConfiguration configuration, IServiceCollection services)
    {
        // Bound from the "PayPal" section; values arrive via environment variables
        // (PAYPAL_CLIENT_ID etc., bridged by the host) or user-secrets. Never hard-coded.
        var section = configuration.GetSection(PaymentGatewayOptions.SectionName);
        var payPalOptions = new PaymentGatewayOptions
        {
            ClientId = section["ClientId"] ?? string.Empty,
            ClientSecret = section["ClientSecret"] ?? string.Empty,
            Environment = section["Environment"] ?? "sandbox",
            Currency = section["Currency"] ?? "USD",
            BaseUrl = section["BaseUrl"]
        };
        services.AddSingleton(payPalOptions);

        services.AddSingleton<IPaymentGateway>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<PayPalPaymentGateway>();
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            return new PayPalPaymentGateway(new HttpClient(handler), payPalOptions, logger);
        });

        services.AddSingleton<OrderOperationLock>();
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
    }
}
