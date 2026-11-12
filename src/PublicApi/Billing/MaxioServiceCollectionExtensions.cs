using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>Remembers the body of the last non-success Maxio response so the reason can be handed to the caller.</summary>
public sealed class ErrorBodyCapture
{
    private static readonly AsyncLocal<ErrorBodyCapture?> _current = new();
    public static ErrorBodyCapture? Current { get => _current.Value; set => _current.Value = value; }
    public int? Status { get; set; }
    public string? Body { get; set; }
}

public sealed class ErrorBodyCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var capture = ErrorBodyCapture.Current;
        if (capture != null && !response.IsSuccessStatusCode && response.Content != null)
        {
            await response.Content.LoadIntoBufferAsync();
            capture.Status = (int)response.StatusCode;
            capture.Body = await response.Content.ReadAsStringAsync();
        }
        return response;
    }
}

public static class MaxioServiceCollectionExtensions
{
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        // Fails the host start when a setting is missing (the SDK would only throw on the first call).
        var settings = MaxioSettings.LoadAndValidate(configuration);

        services.AddSingleton(settings);
        services.AddTransient<ErrorBodyCaptureHandler>();
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(8))
            .AddHttpMessageHandler<ErrorBodyCaptureHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // Options are built once, here, and captured by the singleton client.
        services.AddSingleton(sp =>
        {
            var options = new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" },
                Environment = settings.IsEu ? ServerEnvironment.Eu : ServerEnvironment.Us,
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(8), MaxRetries = 2 },
            };
            options.Logging = new LoggingOptions { LoggerFactory = sp.GetRequiredService<ILoggerFactory>() };
            options.Server.Production.Us.Site = settings.Subdomain!;
            options.Server.Production.Eu.Site = settings.Subdomain!;
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                // Optional override: used verbatim as the API base address.
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
                options.Server.Production.Eu.BaseUrl = settings.BaseUrl;
            }

            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(http, options);
        });

        services.AddScoped<MaxioBillingService>();
        return services;
    }
}
