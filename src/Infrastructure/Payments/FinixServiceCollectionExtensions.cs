using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Core.Hooks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class FinixServiceCollectionExtensions
{
    public static IServiceCollection AddFinixPaymentGateway(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<FinixOptions>>().Value;
            Validate(options);

            var sdkOptions = new FinixApiReferenceClientOptions
            {
                BasicAuth = new BasicAuthCredentials
                {
                    Username = options.Username,
                    Password = options.Password
                },
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetService<ILoggerFactory>(),
                    LogRequestHeaders = false,
                    LogResponseHeaders = false,
                    LogRequestBody = false
                }
            };
            sdkOptions.Server.Default.Production.BaseUrl = options.ResolveBaseUrl();
            sdkOptions.Hooks = new[] { NormalizeDateQueryHook() };

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new FinixApiReferenceClient(httpClient, sdkOptions);
        });

        services.AddScoped<IFinixPaymentGateway, FinixPaymentGateway>();
        return services;
    }

    private static void Validate(FinixOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Username))
            throw new InvalidOperationException("Finix:Username is required.");
        if (string.IsNullOrWhiteSpace(options.Password))
            throw new InvalidOperationException("Finix:Password is required.");
        if (string.IsNullOrWhiteSpace(options.MerchantId))
            throw new InvalidOperationException("Finix:MerchantId is required.");
        if (string.IsNullOrWhiteSpace(options.Environment))
            throw new InvalidOperationException("Finix:Environment is required.");
        if (string.IsNullOrWhiteSpace(options.Currency))
            throw new InvalidOperationException("Finix:Currency is required.");
    }

    private static SdkHook NormalizeDateQueryHook() =>
        SdkHook.OnRequest((request, _) =>
        {
            if (request.RequestUri is null)
                return;

            var builder = new UriBuilder(request.RequestUri);
            var query = builder.Query.TrimStart('?');
            if (query.Length == 0)
                return;

            var pairs = query.Split('&');
            for (var i = 0; i < pairs.Length; i++)
            {
                var separator = pairs[i].IndexOf('=');
                if (separator < 0)
                    continue;

                var rawKey = pairs[i][..separator];
                var key = Uri.UnescapeDataString(rawKey);
                if (!key.EndsWith(".gte", StringComparison.Ordinal) &&
                    !key.EndsWith(".lte", StringComparison.Ordinal))
                    continue;

                var value = Uri.UnescapeDataString(pairs[i][(separator + 1)..]);
                if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
                    continue;

                pairs[i] = $"{rawKey}={date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}";
            }

            builder.Query = string.Join("&", pairs);
            request.RequestUri = builder.Uri;
        });
}
