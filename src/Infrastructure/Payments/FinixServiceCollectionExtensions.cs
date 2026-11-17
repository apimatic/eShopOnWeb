using System;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class FinixServiceCollectionExtensions
{
    public static IServiceCollection AddFinixPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FinixOptions>(configuration.GetSection(FinixOptions.SectionName));
        services.AddHttpClient();

        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<FinixOptions>>().Value;
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(FinixApiReferenceClient));

            var clientOptions = new FinixApiReferenceClientOptions
            {
                Environment = ServerEnvironment.Production,
                Logging = new LoggingOptions
                {
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>()
                },
                Server = new ServerOptions
                {
                    Default = new DefaultOptions
                    {
                        Production = new DefaultOptions.ProductionOptions
                        {
                            BaseUrl = ResolveBaseUrl(options)
                        }
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(options.Username) &&
                !string.IsNullOrWhiteSpace(options.Password))
            {
                clientOptions.BasicAuth = new BasicAuthCredentials
                {
                    Username = options.Username,
                    Password = options.Password
                };
            }

            return new FinixApiReferenceClient(httpClient, clientOptions);
        });

        services.AddScoped<IPaymentService, FinixPaymentService>();
        return services;
    }

    private static string ResolveBaseUrl(FinixOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.Trim();
        }

        if (string.Equals(options.Environment, "live", StringComparison.OrdinalIgnoreCase))
        {
            return "https://finix.live-payments-api.com";
        }

        return new DefaultOptions().Production.BaseUrl;
    }
}
