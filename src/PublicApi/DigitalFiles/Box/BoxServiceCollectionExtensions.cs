using System;
using System.Net;
using System.Net.Http;
using BoxPlatformApi;
using BoxPlatformApi.Core.Configuration;
using BoxPlatformApi.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.DigitalFiles.Box;

public static class BoxServiceCollectionExtensions
{
    public const string HttpClientName = "Box";

    /// <summary>
    /// Registers the Box-backed <see cref="IDigitalFileStorage"/>. The host refuses to start when
    /// <c>Box:AccessToken</c> is missing or blank.
    /// </summary>
    public static IServiceCollection AddBoxDigitalFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BoxOptions>()
            .Bind(configuration.GetSection(BoxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMemoryCache();
        services.AddSingleton<BoxAccessTokenStrategy>();

        services.AddHttpClient(HttpClientName, (sp, httpClient) =>
            {
                // Per-attempt backstop; the SDK's own per-attempt timeout (AttemptTimeout) fires first. With
                // ResponseHeadersRead this bounds only the wait for response headers, never a download body.
                httpClient.Timeout = sp.GetRequiredService<IOptions<BoxOptions>>().Value.AttemptTimeout + TimeSpan.FromSeconds(5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding this HttpClient: recycle connections so DNS changes apply.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                // Box answers downloads with a redirect to its download host.
                AllowAutoRedirect = true,
                // Keep bytes and Content-Length exactly as Box sent them.
                AutomaticDecompression = DecompressionMethods.None,
            });

        services.AddSingleton(sp =>
        {
            var box = sp.GetRequiredService<IOptions<BoxOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var options = new BoxPlatformApiClientOptions
            {
                Environment = ServerEnvironment.Production,
                // Pre-issued token: the strategy replaces the interactive authorization-code flow. The credentials
                // object must be non-null or the SDK sends requests unauthenticated.
                OAuth2Security = BoxAccessTokenStrategy.PlaceholderCredentials,
                OAuth2SecurityTokenStrategy = sp.GetRequiredService<BoxAccessTokenStrategy>(),
                // Every Box call in scope is a GET, so SDK retries are safe. Worst case per call:
                // 3 attempts x AttemptTimeout + ~3.5 s backoff; the RequestTimeout deadline bounds the total.
                Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = box.AttemptTimeout },
                // Assigned explicitly so the BOXPLATFORMAPICLIENT_LOG variable cannot switch on body logging.
                Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() },
                TimeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System,
            };
            return new BoxPlatformApiClient(httpClient, options);
        });

        services.AddSingleton<IDigitalFileStorage, BoxDigitalFileStorage>();
        return services;
    }
}
