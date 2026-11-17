using System;
using System.Net.Http;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class FinixServiceCollectionExtensions
{
    public static IServiceCollection AddFinixPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FinixSettings>(configuration.GetSection("Finix"));
        services.AddHttpClient("Finix");
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<FinixSettings>>().Value;
            var options = new FinixApiReferenceClientOptions
            {
                // Never let the SDK log request bodies or headers: they contain card data.
                Logging = new LoggingOptions
                {
                    LoggerFactory = NullLoggerFactory.Instance,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                },
                Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Production = new DefaultOptions.ProductionOptions
                        {
                            BaseUrl = ResolveBaseUrl(settings)
                        }
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(settings.Username)
                && !string.IsNullOrWhiteSpace(settings.Password))
            {
                options.BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.Username,
                    Password = settings.Password
                };
            }

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Finix");
            httpClient.Timeout = TimeSpan.FromSeconds(100);
            return new FinixApiReferenceClient(httpClient, options);
        });

        services.AddSingleton<KeyedAsyncLock>();
        services.AddScoped<IPaymentService, FinixPaymentService>();

        return services;
    }

    private static string ResolveBaseUrl(FinixSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl.Trim();
        }

        var environment = (settings.Environment ?? "sandbox").Trim();
        if (environment.Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            return "https://finix.live-payments-api.com";
        }

        return "https://finix.sandbox-payments-api.com";
    }
}
