using System;
using System.Net.Http;
using BoxPlatformApi;
using BoxPlatformApi.Core.Authentication.OAuth2.AuthorizationCode;
using BoxPlatformApi.Core.Configuration;
using BoxPlatformApi.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.DigitalFiles.Box;

public static class BoxServiceCollectionExtensions
{
    public const string HttpClientName = "Box";

    /// <summary>The environment variable the merchant's token arrives in; mapped onto <c>Box:AccessToken</c>.</summary>
    public const string AccessTokenEnvironmentVariable = "BOX_ACCESS_TOKEN";

    /// <summary>
    /// Maps <c>BOX_ACCESS_TOKEN</c> onto <c>Box:AccessToken</c> when no other source has set that key,
    /// so user-secrets or <c>Box__AccessToken</c> take precedence.
    /// </summary>
    public static IConfigurationBuilder AddBoxAccessTokenFromEnvironment(this IConfigurationManager configuration)
    {
        var configured = configuration[$"{BoxOptions.SectionName}:{nameof(BoxOptions.AccessToken)}"];
        var fromEnvironment = Environment.GetEnvironmentVariable(AccessTokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured) && !string.IsNullOrWhiteSpace(fromEnvironment))
        {
            configuration.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>(
                    $"{BoxOptions.SectionName}:{nameof(BoxOptions.AccessToken)}", fromEnvironment),
            });
        }
        return configuration;
    }

    /// <summary>
    /// Registers the Box-backed <see cref="IDigitalFileProvider"/>. The host refuses to start when
    /// <c>Box:AccessToken</c> is missing or blank.
    /// </summary>
    public static IServiceCollection AddBoxDigitalFiles(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BoxOptions>()
            .Bind(configuration.GetSection(BoxOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<BoxOptions>, BoxOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, (sp, client) =>
            {
                // Per attempt, until response headers; download bodies are bounded by the stall guard instead.
                var options = sp.GetRequiredService<IOptions<BoxOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding this HttpClient: recycle connections so DNS stays fresh.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                // Box answers a content download with a redirect to its download host.
                AllowAutoRedirect = true,
            });

        services.AddSingleton<BoxConfiguredTokenStrategy>();

        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<BoxOptions>>().Value;
            var attemptTimeout = TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds);
            var options = new BoxPlatformApiClientOptions
            {
                Environment = ServerEnvironment.Production,
                TimeProvider = sp.GetRequiredService<TimeProvider>(),
                // The token comes from configuration via the strategy; these credentials are never sent and
                // the interactive prompt is never reached.
                OAuth2Security = new OAuth2AuthorizationCodeCredentials
                {
                    ClientId = "eshop-configured-token",
                    RedirectUri = "urn:eshop:not-used",
                    PromptForAuthorizationCode = (_, _) => throw new InvalidOperationException(
                        "Interactive Box authorization is not supported; configure Box:AccessToken."),
                },
                OAuth2SecurityTokenStrategy = sp.GetRequiredService<BoxConfiguredTokenStrategy>(),
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = settings.MaxRetries,
                    Timeout = attemptTimeout,
                },
            };
            // Assigned explicitly so the SDK's log environment variable cannot turn on header/body logging.
            options.Logging = options.Logging with
            {
                LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            };

            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new BoxPlatformApiClient(httpClient, options);
        });

        services.AddSingleton<IDigitalFileProvider, BoxDigitalFileProvider>();
        return services;
    }
}
