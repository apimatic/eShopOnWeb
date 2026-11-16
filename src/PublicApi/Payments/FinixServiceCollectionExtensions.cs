using System;
using System.Globalization;
using System.Net.Http;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Core.Hooks;
using FinixApiReference.Models.Enums;
using FinixApiReference.Servers;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public static class FinixServiceCollectionExtensions
{
    public static IServiceCollection AddFinixPayments(this IServiceCollection services, IConfiguration configuration)
    {
        var options = BindAndValidate(configuration);

        services.AddSingleton(options);
        services.AddHttpClient(FinixPaymentGateway.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var finixOptions = sp.GetRequiredService<FinixOptions>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(FinixPaymentGateway.HttpClientName);
            var clientOptions = new FinixApiReferenceClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = finixOptions.Username,
                    Password = finixOptions.Password
                },
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Disabled(),
                TimeProvider = TimeProvider.System,
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                },
                Hooks =
                [
                    SdkHook.OnRequest((request, _) => FinixDateQueryHook.Apply(request))
                ]
            };

            if (!string.IsNullOrWhiteSpace(finixOptions.BaseUrl))
            {
                clientOptions.Server.Default.Production.BaseUrl = finixOptions.BaseUrl!;
            }

            return new FinixApiReferenceClient(httpClient, clientOptions);
        });

        services.AddScoped<IFinixPaymentGateway, FinixPaymentGateway>();
        services.AddScoped<IPaymentApplicationService, PaymentApplicationService>();
        return services;
    }

    private static FinixOptions BindAndValidate(IConfiguration configuration)
    {
        var options = new FinixOptions
        {
            Username = configuration["Finix:Username"] ?? string.Empty,
            Password = configuration["Finix:Password"] ?? string.Empty,
            MerchantId = configuration["Finix:MerchantId"] ?? string.Empty,
            Environment = configuration["Finix:Environment"] ?? string.Empty,
            Currency = configuration["Finix:Currency"] ?? string.Empty,
            BaseUrl = configuration["Finix:BaseUrl"]
        };

        Require(options.Username, "Finix:Username");
        Require(options.Password, "Finix:Password");
        Require(options.MerchantId, "Finix:MerchantId");
        Require(options.Environment, "Finix:Environment");
        Require(options.Currency, "Finix:Currency");

        if (!string.Equals(options.Environment, "sandbox", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Environment, "live", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Finix:Environment must be 'sandbox' or 'live'.");
        }

        if (!Currency.TryGetKnownValue(options.Currency.Trim().ToUpperInvariant(), out _))
        {
            throw new InvalidOperationException(
                "Finix:Currency is not a currency code this SDK declares (USD or CAD).");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Finix:BaseUrl must be an absolute URL when it is set.");
        }

        if (string.Equals(options.Environment, "live", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException(
                "Finix:BaseUrl is required when Finix:Environment is 'live' because this SDK declares no live host.");
        }

        return options;
    }

    private static void Require(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it through the environment, user-secrets or your secret store before starting the app.");
        }
    }
}

/// <summary>
/// The generated SDK formats DateTimeOffset query values as
/// <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>, while Finix's transfer list filter requires
/// <c>yyyy-MM-ddTHH:mm:ss</c>. This hook rewrites only those two query values on the
/// transfers collection request, leaving every other request untouched.
/// </summary>
internal static class FinixDateQueryHook
{
    public static void Apply(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri is null ||
            !uri.AbsolutePath.EndsWith("/transfers", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(uri.Query))
        {
            return;
        }

        var parts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var changed = false;

        for (var i = 0; i < parts.Length; i++)
        {
            var pair = parts[i].Split('=', 2);
            if (pair.Length != 2 ||
                (!string.Equals(pair[0], "created_at.gte", StringComparison.Ordinal) &&
                 !string.Equals(pair[0], "created_at.lte", StringComparison.Ordinal)))
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[1]);
            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                continue;
            }

            parts[i] = $"{pair[0]}={Uri.EscapeDataString(parsed.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))}";
            changed = true;
        }

        if (changed)
        {
            request.RequestUri = new UriBuilder(uri) { Query = string.Join("&", parts) }.Uri;
        }
    }
}
