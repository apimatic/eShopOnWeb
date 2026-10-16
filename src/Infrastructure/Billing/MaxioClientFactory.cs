using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public static class MaxioClientFactory
{
    /// <summary>Bounds one attempt. GETs are retried; POSTs never are (the SDK default, deliberately kept).</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Backstop on the HttpClient itself; above <see cref="AttemptTimeout"/> so it only fires if that one does not.</summary>
    public static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(15);

    public const int MaxGetRetries = 2;

    /// <summary>Basic-auth password Maxio expects alongside an API key (a protocol constant, not a secret).</summary>
    private const string ApiKeyPassword = "x";

    public static MaxioAdvancedBillingClientOptions CreateOptions(MaxioSettings settings, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        var environment = string.Equals(settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? ServerEnvironment.Eu
            : ServerEnvironment.Us;

        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = environment,
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = ApiKeyPassword },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = MaxGetRetries,
                Timeout = AttemptTimeout,
            },
            // Customer requests carry names and e-mail addresses: never log bodies. Assigning the factory explicitly
            // also keeps the MAXIOADVANCEDBILLINGCLIENT_LOG environment variable from switching body logging on.
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
            },
            TimeProvider = timeProvider,
        };

        // Only the selected environment's server options are ever read, so configure that one.
        var production = options.Server.Production;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            if (environment == ServerEnvironment.Eu) production.Eu.BaseUrl = settings.BaseUrl;
            else production.Us.BaseUrl = settings.BaseUrl;
        }
        else
        {
            if (environment == ServerEnvironment.Eu) production.Eu.Site = settings.Subdomain!;
            else production.Us.Site = settings.Subdomain!;
        }

        return options;
    }
}
