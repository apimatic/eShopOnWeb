using System;
using System.Collections.Generic;
using System.Net.Http;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Models.Enums;
using FinixApiReference.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public static class FinixServiceCollectionExtensions
{
    public const string HttpClientName = "Finix";

    public static void AddFinixPayments(this IServiceCollection services, ConfigurationManager configuration)
    {
        MapEnvironmentVariables(configuration);

        var options = configuration.GetSection("Finix").Get<FinixOptions>() ?? new FinixOptions();
        Validate(options);

        services.Configure<FinixOptions>(configuration.GetSection("Finix"));
        services.AddHttpContextAccessor();

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var clientOptions = new FinixApiReferenceClientOptions
            {
                Environment = ServerEnvironment.Production,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = options.Username,
                    Password = options.Password
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(10),
                    MaxRetries = 2,
                    HttpMethodsToRetry = new[] { HttpMethod.Get }
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    LogRequestBody = false,
                    LogRequestHeaders = false,
                    LogResponseHeaders = false
                }
            };

            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                clientOptions.Server.Default.Production.BaseUrl = options.BaseUrl!;
            }

            return new FinixApiReferenceClient(httpClient, clientOptions);
        });

        services.AddSingleton<IFinixGateway, FinixGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
    }

    public static Currency ResolveCurrency(string currency)
    {
        if (Currency.TryGetKnownValue(currency?.Trim().ToUpperInvariant(), out var known))
        {
            return known;
        }

        throw new InvalidOperationException(
            "Finix:Currency must be a currency this Finix SDK can express (USD or CAD).");
    }

    private static void MapEnvironmentVariables(ConfigurationManager configuration)
    {
        var additions = new Dictionary<string, string?>();
        Map("Finix:Username", "FINIX_USERNAME");
        Map("Finix:Password", "FINIX_PASSWORD");
        Map("Finix:MerchantId", "FINIX_MERCHANT_ID");
        Map("Finix:Environment", "FINIX_ENVIRONMENT");
        Map("Finix:Currency", "FINIX_CURRENCY");
        Map("Finix:BaseUrl", "FINIX_BASE_URL");

        if (additions.Count > 0)
        {
            configuration.AddInMemoryCollection(additions);
        }

        void Map(string configurationKey, string environmentVariable)
        {
            if (!string.IsNullOrWhiteSpace(configuration[configurationKey]))
            {
                return;
            }

            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                additions[configurationKey] = value;
            }
        }
    }

    private static void Validate(FinixOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Username))
        {
            throw new InvalidOperationException("Finix:Username is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("Finix:Password is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.MerchantId))
        {
            throw new InvalidOperationException("Finix:MerchantId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Currency))
        {
            throw new InvalidOperationException("Finix:Currency is not configured.");
        }

        _ = ResolveCurrency(options.Currency);

        if (string.IsNullOrWhiteSpace(options.Environment))
        {
            throw new InvalidOperationException("Finix:Environment is not configured.");
        }

        var environment = options.Environment.Trim().ToLowerInvariant();
        if (environment is not ("sandbox" or "live" or "production"))
        {
            throw new InvalidOperationException("Finix:Environment must be 'sandbox' or 'live'.");
        }

        if (environment is "live" or "production" && string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException(
                "Finix:BaseUrl must be set when Finix:Environment is 'live'; this SDK build declares only the sandbox host.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException("Finix:BaseUrl must be an absolute http or https URL.");
        }
    }
}
