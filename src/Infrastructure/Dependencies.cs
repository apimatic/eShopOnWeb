using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
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

    public static void ConfigureMaxioServices(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetRequiredSection(MaxioSettings.SectionName))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ApiKey), "Maxio:ApiKey is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.BaseUrl) || !string.IsNullOrWhiteSpace(settings.Subdomain),
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.")
            .Validate(settings => string.IsNullOrWhiteSpace(settings.BaseUrl) ||
                                  Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                "Maxio:BaseUrl must be an absolute HTTPS URL when set.")
            .ValidateOnStart();

        services.AddSingleton<MaxioWriteGuard>();
        services.AddTransient<MaxioWriteGuardHandler>();
        services.AddHttpClient("Maxio", client => client.Timeout = TimeSpan.FromSeconds(10))
            .AddHttpMessageHandler<MaxioWriteGuardHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioSettings>>().Value;
            var options = new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Timeout = TimeSpan.FromSeconds(10)
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

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Maxio");
            return new MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();
    }
}
