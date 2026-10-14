using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public static class MaxioServiceCollectionExtensions
{
    /// <summary>Name of the dedicated HttpClient the Maxio SDK client is built over.</summary>
    public const string HttpClientName = "MaxioAdvancedBilling";

    public static IServiceCollection AddMaxioSubscriptionBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetSection(MaxioOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaxioOptions>, MaxioOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHttpClient(HttpClientName, (sp, httpClient) =>
            {
                var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
                // Per-attempt backstop just above the SDK's own per-attempt timeout; the whole-call bound is the
                // operation budget token (MaxioCallBudget).
                httpClient.Timeout = TimeSpan.FromSeconds(maxio.AttemptTimeoutSeconds + 5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // The SDK client below is a singleton holding one HttpClient; recycle pooled connections so DNS
                // changes on Maxio's side are picked up without a restart.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        services.AddSingleton(sp =>
        {
            // Options are built once here: a rotated API key takes effect on the next process restart.
            var maxio = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new MaxioAdvancedBillingClient(httpClient, BuildClientOptions(maxio, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
        });

        services.AddSingleton<MaxioCallBudget>();
        services.AddScoped<ISubscriptionPlanCatalog, MaxioSubscriptionPlanCatalog>();
        services.AddScoped<MaxioCustomerProvisioner>();
        services.AddScoped<ISubscriptionBillingService, MaxioSubscriptionBillingService>();

        return services;
    }

    internal static MaxioAdvancedBillingClientOptions BuildClientOptions(MaxioOptions maxio, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            TimeProvider = timeProvider,
            BasicAuth = new BasicAuthCredentials
            {
                Username = maxio.ApiKey!,
                // Documented by the provider: the API key is the username and the password is the literal "x".
                Password = "x"
            },
            // GET/HEAD/PUT/OPTIONS only (the default): the two POSTs in scope are never resent by the SDK.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(maxio.AttemptTimeoutSeconds) },
            Logging = new LoggingOptions
            {
                // Assigned explicitly so the MAXIOADVANCEDBILLINGCLIENT_LOG variable cannot turn on body logging.
                LoggerFactory = loggerFactory,
                // Customer create bodies carry personal data (name, email).
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false
            }
        };

        if (!string.IsNullOrWhiteSpace(maxio.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = maxio.BaseUrl;
        }
        else
        {
            options.Server.Production.Us.Site = maxio.Subdomain!.Trim();
        }

        return options;
    }
}
