using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Captures the last HTTP status so a JsonException thrown while mapping a
/// non-2xx body can still be treated as a rejection rather than an outage.
/// </summary>
internal static class LastHttpStatus
{
    private static readonly AsyncLocal<HttpStatusCode?> Status = new();

    public static HttpStatusCode? Current
    {
        get => Status.Value;
        set => Status.Value = value;
    }
}

internal sealed class LastHttpStatusHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastHttpStatus.Current = response.StatusCode;
        return response;
    }
}

/// <summary>
/// Marks a write that must not be resent by the SDK transport-retry pipeline.
/// </summary>
internal sealed class OnceWriteScope : IDisposable
{
    private static readonly AsyncLocal<OnceWriteScope?> CurrentLocal = new();

    public int WriteSends { get; set; }

    public static OnceWriteScope? Current => CurrentLocal.Value;

    public static OnceWriteScope Begin()
    {
        var scope = new OnceWriteScope();
        CurrentLocal.Value = scope;
        return scope;
    }

    public void Dispose()
    {
        if (ReferenceEquals(CurrentLocal.Value, this))
        {
            CurrentLocal.Value = null;
        }
    }
}

/// <summary>
/// Sentinel: do not derive from HttpRequestException — the retry pipeline would resend.
/// </summary>
internal sealed class DuplicateWriteRefusedException : Exception
{
    public DuplicateWriteRefusedException()
        : base("A duplicate write was refused after the first attempt was already sent.")
    {
    }
}

internal sealed class OnceWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isWrite = request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Delete;

        if (isWrite && OnceWriteScope.Current is { } scope)
        {
            if (scope.WriteSends > 0)
            {
                throw new DuplicateWriteRefusedException();
            }

            scope.WriteSends++;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
