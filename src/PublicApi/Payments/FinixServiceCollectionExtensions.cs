using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Core.Hooks;
using FinixApiReference.Models.Enums;
using FinixApiReference.Servers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class FinixServiceCollectionExtensions
{
    public const string HttpClientName = "Finix";

    public static IServiceCollection AddFinixPayments(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var finixOptions = FinixOptions.FromConfiguration(configuration);
        finixOptions.Validate();
        var currency = ResolveCurrency(finixOptions.Currency!);

        services.AddSingleton(finixOptions);

        services.AddHttpClient(HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton<IFinixGateway>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var clientOptions = BuildClientOptions(finixOptions, loggerFactory);
            var client = new FinixApiReferenceClient(httpClient, clientOptions);

            return new FinixGateway(client, finixOptions, currency);
        });

        return services;
    }

    private static FinixApiReferenceClientOptions BuildClientOptions(
        FinixOptions finixOptions,
        ILoggerFactory loggerFactory)
    {
        var environment = (finixOptions.Environment ?? string.Empty).Trim().ToLowerInvariant();

        if (environment is not ("sandbox" or "live"))
        {
            throw new InvalidOperationException(
                "Finix:Environment must be either 'sandbox' or 'live'.");
        }

        if (environment == "live" && !finixOptions.HasBaseUrl)
        {
            throw new InvalidOperationException(
                "Finix:Environment is 'live' but the SDK ships only a sandbox server node. " +
                "Set Finix:BaseUrl to the live API base URL for this deployment.");
        }

        var options = new FinixApiReferenceClientOptions
        {
            Environment = ServerEnvironment.Production,
            BasicAuth = new BasicAuthCredentials
            {
                Username = finixOptions.Username!,
                Password = finixOptions.Password!
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 2,
                Timeout = TimeSpan.FromSeconds(10),
                HttpMethodsToRetry = new[] { HttpMethod.Get, HttpMethod.Head }
            },
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
                LogRequestBody = false,
                RedactedKeys = new[]
                {
                    "sig", "signature", "access_token", "apikey", "api_key",
                    "client_secret", "password", "refresh_token", "code",
                    "assertion", "client_assertion", "number", "security_code",
                    "cvv", "cvc", "expiration_month", "expiration_year", "name"
                }
            },
            Hooks = new[]
            {
                SdkHook.OnRequest((request, _) =>
                {
                    if (request.RequestUri is not null)
                    {
                        request.RequestUri = NormalizeFinixDateQuery(request.RequestUri);
                    }
                })
            }
        };

        if (finixOptions.HasBaseUrl)
        {
            options.Server.Default.Production.BaseUrl = finixOptions.BaseUrl!;
        }

        return options;
    }

    private static Currency ResolveCurrency(string rawCurrency)
    {
        if (!Currency.TryGetKnownValue(rawCurrency, out var currency))
        {
            throw new InvalidOperationException(
                "Finix:Currency is not a supported ISO 4217 value for this SDK. " +
                $"Supported values: {string.Join(", ", Currency.GetKnownValues().Select(c => c.Value))}.");
        }

        return currency;
    }

    private static readonly string[] FinixDateQueryKeys =
    [
        "created_at.gte", "created_at.lte",
        "updated_at.gte", "updated_at.lte",
        "ready_to_settle_at.gte", "ready_to_settle_at.lte"
    ];

    // Finix accepts date filters as a bare ISO-8601 date-time (no fractional
    // seconds, no offset). The generated SDK formats them with milliseconds and
    // a Z suffix, so normalize them at the SDK's own request hook.
    private static Uri NormalizeFinixDateQuery(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query))
        {
            return uri;
        }

        var parsed = QueryHelpers.ParseQuery(uri.Query);
        var changed = false;
        var pairs = new List<string>();

        foreach (var pair in parsed)
        {
            var value = pair.Value.ToString();

            if (FinixDateQueryKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
                && DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedDate))
            {
                value = parsedDate.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                changed = true;
            }

            pairs.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(value)}");
        }

        if (!changed)
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Join("&", pairs)
        };

        return builder.Uri;
    }
}
