using System;
using System.Net.Http;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        ConfigureFinix(configuration, services);
    }

    private static void ConfigureFinix(IConfiguration configuration, IServiceCollection services)
    {
        var section = configuration.GetSection(FinixOptions.SectionName);
        services.Configure<FinixOptions>(section);

        var finixOptions = section.Get<FinixOptions>() ?? new FinixOptions();
        finixOptions.Username = finixOptions.Username?.Trim() ?? string.Empty;
        finixOptions.Password = finixOptions.Password?.Trim() ?? string.Empty;
        finixOptions.MerchantId = finixOptions.MerchantId?.Trim() ?? string.Empty;
        finixOptions.Currency = string.IsNullOrWhiteSpace(finixOptions.Currency)
            ? "USD"
            : finixOptions.Currency.Trim();

        services.AddHttpClient("finix", client => client.Timeout = TimeSpan.FromSeconds(120));

        services.AddSingleton<IFinixPaymentGateway>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("finix");

            var clientOptions = new FinixApiReferenceClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = finixOptions.Username,
                    Password = finixOptions.Password
                },
                Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Production = new DefaultOptions.ProductionOptions
                        {
                            BaseUrl = finixOptions.ResolveBaseUrl()
                        }
                    }
                },
                // Card data must never be written to logs. The SDK redacts known
                // secret keys by default, and request/response bodies stay off.
                Logging = new LoggingOptions
                {
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                },
                Hooks = new[] { FinixQueryHooks.NormalizeDatePrecision }
            };

            return new FinixPaymentGateway(
                new FinixApiReferenceClient(httpClient, clientOptions), finixOptions);
        });

        services.AddScoped<IPaymentService, PaymentService>();
    }
}
