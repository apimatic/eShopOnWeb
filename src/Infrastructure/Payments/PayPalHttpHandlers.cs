using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class DuplicateWritePreventedException : Exception
{
    public DuplicateWritePreventedException() : base("A retried write was blocked after the first send.") { }
}

internal sealed class SingleSendScope : IDisposable
{
    private static readonly AsyncLocal<State?> Current = new();
    private readonly State? _previous;
    private bool _disposed;

    private SingleSendScope(State? previous)
    {
        _previous = previous;
    }

    public static SingleSendScope Enter()
    {
        var scope = new SingleSendScope(Current.Value);
        Current.Value = new State();
        return scope;
    }

    public static bool TryMarkSent()
    {
        var state = Current.Value;
        if (state is null) return true;
        if (state.Sent) return false;
        state.Sent = true;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Current.Value = _previous;
        _disposed = true;
    }

    private sealed class State
    {
        public bool Sent { get; set; }
    }
}

internal sealed class SingleWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var isToken = path.Contains("/v1/oauth2/token", StringComparison.OrdinalIgnoreCase);
        var isWrite = !isToken && (
            request.Method == HttpMethod.Post ||
            request.Method == HttpMethod.Put ||
            request.Method == HttpMethod.Patch ||
            request.Method == HttpMethod.Delete);

        if (isWrite && !SingleSendScope.TryMarkSent())
            throw new DuplicateWritePreventedException();

        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class PayPalStatusCaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<int?> LastStatus = new();

    public static int? LastHttpStatus => LastStatus.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        LastStatus.Value = (int)response.StatusCode;
        return response;
    }
}
