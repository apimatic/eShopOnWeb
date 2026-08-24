using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure;

public static class Dependencies
{
    public static void ConfigureServices(IConfiguration configuration, IServiceCollection services)
    {
        var useOnlyInMemoryDatabase = configuration.GetValue<bool>("UseOnlyInMemoryDatabase");

        if (useOnlyInMemoryDatabase)
        {
            services.AddDbContext<CatalogContext>(c => c.UseInMemoryDatabase("Catalog"));
            services.AddDbContext<AppIdentityDbContext>(options => options.UseInMemoryDatabase("Identity"));
        }
        else
        {
            services.AddDbContext<CatalogContext>(c =>
                c.UseSqlServer(configuration.GetConnectionString("CatalogConnection")));
            services.AddDbContext<AppIdentityDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("IdentityConnection")));
        }
    }

    public static void ConfigureMaxioBilling(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetRequiredSection(MaxioOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "Maxio:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Subdomain), "Maxio:Subdomain is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(options => string.IsNullOrWhiteSpace(options.BaseUrl) || Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "Maxio:BaseUrl must be an absolute URL when set.")
            .ValidateOnStart();

        services.AddTransient<WriteOnceHandler>();
        services.AddHttpClient("Maxio", client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<WriteOnceHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioOptions>>().Value;
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio");
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
                },
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                }
            };

            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<AsyncKeyedLocker>();
        services.AddSingleton<IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
    }
}
