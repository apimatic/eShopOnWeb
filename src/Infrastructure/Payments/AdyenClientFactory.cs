using AdyenApIs;
using AdyenApIs.Core.Configuration;
using AdyenApIs.Servers;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class AdyenClientFactory
{
    /// <summary>The named HttpClient that carries Adyen traffic (its own pipeline, timeout and connection lifetime).</summary>
    public const string HttpClientName = "Adyen";

    public static AdyenApIsClientOptions CreateOptions(AdyenSettings settings, ILoggerFactory loggerFactory) => new()
    {
        ApiKeyAuth = settings.ApiKey,
        // The SDK's only environment; its base URLs are Adyen's test endpoints (checkout-test.adyen.com).
        Environment = ServerEnvironment.Production,
        // Never let the SDK resend a write on its own: the gateway owns the single same-key re-send.
        Retry = RetryOptions.Disabled() with { Timeout = settings.AttemptTimeout },
        Logging = new LoggingOptions
        {
            // Assigned explicitly so the SDK's log environment variable cannot switch body logging on.
            LoggerFactory = loggerFactory,
            // Request bodies carry encrypted card data and the holder's name: never log them.
            LogRequestBody = false,
            LogRequestHeaders = false,
            LogResponseHeaders = false,
        },
    };
}
