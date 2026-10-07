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

public static class WikimediaTrendsServiceCollectionExtensions
{
    /// <summary>
    /// Name of the HttpClient that carries every call to Wikimedia EventStreams (kept off the shared default client).
    /// </summary>
    public const string HttpClientName = "WikimediaEventStreams";

    public static IServiceCollection AddWikimediaTrends(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WikimediaTrendsOptions>()
            .Bind(configuration.GetSection(WikimediaTrendsOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.UserAgent), "WikimediaTrends:UserAgent must name the shop and a contact address.")
            .Validate(o => o.IdleTimeout > TimeSpan.Zero && o.ConnectTimeout > TimeSpan.Zero, "WikimediaTrends timeouts must be positive.")
            .Validate(o => o.MaxConnectRetries >= 0 && o.MaxConcurrentWatches > 0 && o.MaxMatches > 0, "WikimediaTrends limits are out of range.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientName, (sp, httpClient) =>
            {
                // Bounds one attempt's wait for the response headers; the watch window bounds everything else.
                httpClient.Timeout = sp.GetRequiredService<IOptions<WikimediaTrendsOptions>>().Value.ConnectTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding this HttpClient: recycle connections so DNS stays fresh.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<WikimediaTrendsOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var userAgent = settings.UserAgent.Trim();

            var options = new WikimediaEventStreamsClientOptions
            {
                Environment = ServerEnvironment.Production,
                Retry = RetryOptions.Default() with
                {
                    Timeout = settings.ConnectTimeout,
                    MaxRetries = settings.MaxConnectRetries
                },
                StreamReadTimeout = settings.IdleTimeout,
                // Assigned explicitly so the SDK's log environment variable cannot switch body/header logging on.
                Logging = new LoggingOptions
                {
                    LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                    LogRequestBody = false
                },
                Hooks =
                [
                    // The SDK sends its own User-Agent; Wikimedia wants one naming this shop and a contact.
                    SdkHook.OnRequest((request, _) =>
                    {
                        request.Headers.Remove("User-Agent");
                        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                    })
                ]
            };

            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl.TrimEnd('/');
            }

            return new WikimediaEventStreamsClient(httpClient, options);
        });

        services.AddSingleton<IWikiEditWatcher, WikiEditWatcher>();
        services.AddScoped<ICatalogTermSource, CatalogTermSource>();

        return services;
    }
}
