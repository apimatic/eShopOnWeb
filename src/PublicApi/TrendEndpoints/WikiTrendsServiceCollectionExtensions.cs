using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WikimediaEventStreams;
using WikimediaEventStreams.Core.Configuration;
using WikimediaEventStreams.Core.Hooks;
using WikimediaEventStreams.Servers;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

public static class WikiTrendsServiceCollectionExtensions
{
    /// <summary>
    /// Name of the HttpClient that carries the Wikimedia stream; kept off the shared default client.
    /// </summary>
    public const string HTTP_CLIENT_NAME = "WikimediaEventStreams";

    public static IServiceCollection AddWikiTrends(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WikiTrendsOptions>()
            .Bind(configuration.GetSection(WikiTrendsOptions.CONFIG_NAME))
            .ValidateDataAnnotations()
            .Validate(o => !string.IsNullOrWhiteSpace(o.UserAgent), "WikiTrends:UserAgent must name the shop and a contact address.")
            .ValidateOnStart();

        services.AddHttpClient(HTTP_CLIENT_NAME, client =>
            {
                // Bounds one attempt's wait for the response headers only; the stream body is bounded by the
                // watcher's cancellation token and the SDK's StreamReadTimeout.
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton; recycle pooled connections so DNS changes are picked up.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(10)
            });

        services.AddSingleton(sp =>
        {
            var trendsOptions = sp.GetRequiredService<IOptions<WikiTrendsOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            var userAgent = trendsOptions.UserAgent;

            var options = new WikimediaEventStreamsClientOptions
            {
                Environment = ServerEnvironment.Production,
                TimeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System,
                // Opening the stream is a GET: the SDK may retry it, and the watcher's connect budget caps the total.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(10),
                    Delay = TimeSpan.FromMilliseconds(500)
                },
                // Assigned explicitly so the SDK's logging environment variable cannot switch body logging on.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                },
                StreamReadTimeout = trendsOptions.NoDataTimeout,
                Hooks =
                [
                    // The SDK stamps its own User-Agent on every request; Wikimedia wants ours.
                    SdkHook.OnRequest((request, _) =>
                    {
                        request.Headers.Remove("User-Agent");
                        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                    })
                ]
            };
            return new WikimediaEventStreamsClient(httpClient, options);
        });

        services.AddSingleton<WikiWatchGate>();
        services.AddSingleton<WikiEditWatcher>();
        services.AddScoped<CatalogTrendTerms>();

        return services;
    }
}
