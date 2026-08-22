using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Logs verb, path, and status only. Request/response bodies are never read so PAN/CVC cannot leak.
/// </summary>
internal sealed class PayPalLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalLoggingHandler> _logger;

    public PayPalLoggingHandler(ILogger<PayPalLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("PayPal --> {Method} {Path}", request.Method, request.RequestUri?.PathAndQuery);
        var response = await base.SendAsync(request, cancellationToken);
        PayPalCallContext.LastStatusCode = (int)response.StatusCode;
        _logger.LogInformation("PayPal <-- {StatusCode} {Method} {Path}", (int)response.StatusCode, request.Method, request.RequestUri?.PathAndQuery);
        return response;
    }
}

internal static class PayPalCallContext
{
    private static readonly AsyncLocal<int?> Status = new();
    public static int? LastStatusCode
    {
        get => Status.Value;
        set => Status.Value = value;
    }
}

internal sealed class PayPalDuplicateSendException : Exception
{
    public PayPalDuplicateSendException() : base("A duplicate PayPal write was blocked.")
    {
    }
}

/// <summary>
/// Holds a write-once marker in AsyncLocal so SDK transport retries cannot resend POST/DELETE.
/// </summary>
internal sealed class PayPalWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<SendGuard?> Guard = new();

    public static IDisposable BeginWrite()
    {
        var guard = new SendGuard();
        Guard.Value = guard;
        return guard;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method) && !IsCredentialRequest(request))
        {
            var guard = Guard.Value;
            if (guard is not null && Interlocked.Exchange(ref guard.Sent, 1) == 1)
            {
                throw new PayPalDuplicateSendException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsCredentialRequest(HttpRequestMessage request) =>
        request.RequestUri?.AbsolutePath.Contains("/v1/oauth2/token", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Delete || method == HttpMethod.Patch || method == HttpMethod.Put;

    private sealed class SendGuard : IDisposable
    {
        public int Sent;
        public void Dispose() => Guard.Value = null;
    }
}
