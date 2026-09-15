using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioWriteAlreadySentException : Exception
{
    public MaxioWriteAlreadySentException()
        : base("A write was already sent to the billing provider for this operation.")
    {
    }
}

internal sealed class WriteOnceState
{
    public bool Sent { get; set; }
}

internal static class MaxioWriteOnceScope
{
    private static readonly AsyncLocal<WriteOnceState?> Current = new();

    public static WriteOnceState? State => Current.Value;

    public static IDisposable Begin()
    {
        Current.Value = new WriteOnceState();
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}

internal static class MaxioLastHttpStatus
{
    private static readonly AsyncLocal<HttpStatusCode?> Current = new();

    public static HttpStatusCode? Value
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}

internal sealed class MaxioStatusCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        MaxioLastHttpStatus.Value = response.StatusCode;
        return response;
    }
}

internal sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method))
        {
            var state = MaxioWriteOnceScope.State;
            if (state is not null)
            {
                if (state.Sent)
                {
                    throw new MaxioWriteAlreadySentException();
                }

                state.Sent = true;
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete;
}
