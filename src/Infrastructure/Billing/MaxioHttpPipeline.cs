using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioHttp
{
    public const string ClientName = "MaxioAdvancedBilling";
}

/// <summary>
/// Per-write AsyncLocal gate: a POST/PATCH/DELETE may leave the process at most once.
/// HTTP request options cannot hold this marker — the SDK builds a fresh request per retry.
/// </summary>
internal static class MaxioWriteGate
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    public static IDisposable BeginWrite()
    {
        var scope = new WriteScope();
        Current.Value = scope;
        return scope;
    }

    public static bool TryClaimSend()
    {
        var scope = Current.Value;
        if (scope is null)
        {
            return true;
        }

        if (scope.Sent)
        {
            return false;
        }

        scope.Sent = true;
        return true;
    }

    private sealed class WriteScope : IDisposable
    {
        public bool Sent { get; set; }

        public void Dispose()
        {
            if (ReferenceEquals(Current.Value, this))
            {
                Current.Value = null;
            }
        }
    }
}

internal sealed class MaxioWriteResendRefusedException : Exception
{
    public MaxioWriteResendRefusedException()
        : base("A duplicate billing write was blocked before it reached the network.")
    {
    }
}

internal static class MaxioLastHttp
{
    private static readonly AsyncLocal<HttpStatusCode?> StatusLocal = new();

    public static HttpStatusCode? Status
    {
        get => StatusLocal.Value;
        set => StatusLocal.Value = value;
    }
}

internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method) && !MaxioWriteGate.TryClaimSend())
        {
            throw new MaxioWriteResendRefusedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch || method == HttpMethod.Delete;
}

internal sealed class MaxioLastStatusHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        MaxioLastHttp.Status = response.StatusCode;
        return response;
    }
}

internal sealed class MaxioRequestLoggingHandler : DelegatingHandler
{
    private readonly ILogger<MaxioRequestLoggingHandler> _logger;

    public MaxioRequestLoggingHandler(ILogger<MaxioRequestLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Maxio {Method} {Uri}", request.Method, Sanitize(request.RequestUri));
        var response = await base.SendAsync(request, cancellationToken);
        _logger.LogInformation("Maxio {StatusCode} {Method} {Uri}", (int)response.StatusCode, request.Method, Sanitize(request.RequestUri));
        return response;
    }

    private static string Sanitize(Uri? uri)
    {
        if (uri is null)
        {
            return string.Empty;
        }

        return uri.GetLeftPart(UriPartial.Path);
    }
}
