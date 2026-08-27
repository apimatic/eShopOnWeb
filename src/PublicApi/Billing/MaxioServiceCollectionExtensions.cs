using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    private const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(options.BaseUrl),
                "Maxio:BaseUrl must be an absolute URL when supplied.")
            .Validate(options => !options.Subdomain.Contains('/') && !options.Subdomain.Contains(':'),
                "Maxio:Subdomain must be a site subdomain, not a URL.");

        services.AddSingleton<MaxioWriteOnceCoordinator>();
        services.AddTransient<MaxioWriteOnceHandler>();
        services.AddTransient<MaxioTelemetryHandler>();
        services.AddHttpClient(HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(8))
            .AddHttpMessageHandler<MaxioWriteOnceHandler>()
            .AddHttpMessageHandler<MaxioTelemetryHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var options = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials
                {
                    Username = settings.ApiKey,
                    Password = "x"
                },
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(8),
                    MaxRetries = 3
                }
            };
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Production.Us.Site = settings.Subdomain;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = settings.BaseUrl;
            }

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBilling.MaxioAdvancedBillingClient(httpClient, options);
        });

        services.AddSingleton<IMaxioBillingGateway, MaxioBillingGateway>();
        services.AddScoped<ISubscriptionEnrollmentStore, SubscriptionEnrollmentStore>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddHealthChecks().AddCheck<MaxioHealthCheck>("maxio");
        return services;
    }
}

public sealed class MaxioTelemetryHandler(ILogger<MaxioTelemetryHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            logger.LogInformation("Maxio {Method} completed with status {StatusCode} in {ElapsedMilliseconds}ms",
                request.Method.Method, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Maxio {Method} transport failed with {ExceptionType} after {ElapsedMilliseconds}ms",
                request.Method.Method, ex.GetType().Name, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
