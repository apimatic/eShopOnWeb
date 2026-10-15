using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the HttpClient the Maxio SDK client runs over (tests swap its primary handler).</summary>
    public const string HTTP_CLIENT_NAME = "Maxio";

    public static IServiceCollection AddMaxioSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioSettings>()
            .Bind(configuration.GetSection(MaxioSettings.CONFIG_NAME))
            .Validate(s => s.Validate().Length == 0, "Maxio configuration is incomplete (see Maxio: settings).")
            .ValidateOnStart();

        // Fail fast with the names of the offending keys (never their values).
        var problems = configuration.GetSection(MaxioSettings.CONFIG_NAME).Get<MaxioSettings>()?.Validate()
                       ?? new MaxioSettings().Validate();
        if (problems.Length > 0)
            throw new InvalidOperationException(string.Join("; ", problems) +
                ". Set them via user-secrets or environment-bound configuration.");

        services.AddHttpClient(HTTP_CLIENT_NAME, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

        // Options are built once here and captured by the singleton: a rotated key needs a restart.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Maxio.Sdk");
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!.Trim(), Password = "x" },
                // Writes (POST) are never resent by the SDK: the default HttpMethodsToRetry excludes them and is left untouched.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 2,
                    Timeout = TimeSpan.FromSeconds(8),
                    OnRetry = a => logger.LogWarning("Retrying Maxio read, attempt {Attempt} after {Delay}", a.AttemptNumber, a.Delay)
                },
                // Explicit factory so MAXIOADVANCEDBILLINGCLIENT_LOG=trace can never force request bodies (customer PII) into logs.
                Logging = new LoggingOptions { LoggerFactory = loggerFactory, LogRequestBody = false }
            };
            options.Server.Production.Us.Site = settings.Subdomain!.Trim();
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                options.Server.Production.Us.BaseUrl = settings.BaseUrl.Trim();

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HTTP_CLIENT_NAME);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddSingleton<MaxioSiteInfo>();
        services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
        services.AddHostedService<MaxioStartupCheck>();
        return services;
    }
}

/// <summary>Verifies the credentials at startup so a bad key is a startup failure, not a first-request 401.</summary>
public class MaxioStartupCheck : IHostedService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioStartupCheck> _logger;

    public MaxioStartupCheck(MaxioAdvancedBillingClient client, ILogger<MaxioStartupCheck> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var site = (await _client.Sites.ReadSite(cancellationToken: cts.Token)).Site;
            if (site?.Test == true)
                _logger.LogInformation("Maxio site {Site} is a test (sandbox) site", site.Subdomain);
            else
                _logger.LogWarning("Maxio site {Site} is NOT reported as a test site", site?.Subdomain);
        }
        catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Maxio rejected the configured credentials (Maxio:ApiKey / Maxio:Subdomain).");
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Maxio startup check could not reach the billing system; continuing");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
