using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Prevents the SDK's transport retry pipeline from re-sending a Maxio POST.</summary>
public sealed class MaxioPostSendGate
{
    private readonly AsyncLocal<PostScope?> _currentScope = new();

    public IDisposable BeginScope()
    {
        var previous = _currentScope.Value;
        _currentScope.Value = new PostScope();
        return new Scope(() => _currentScope.Value = previous);
    }

    public void Record(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post || _currentScope.Value is not { } scope)
        {
            return;
        }

        if (Interlocked.Increment(ref scope.PostCount) != 1)
        {
            throw new MaxioPostRetryBlockedException();
        }
    }

    private sealed class PostScope
    {
        public int PostCount;
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

public sealed class MaxioPostRetryBlockedException : Exception
{
    public MaxioPostRetryBlockedException() : base("A retry of a Maxio write was blocked; reconcile by reference.") { }
}

public sealed class MaxioPostSendGateHandler(MaxioPostSendGate gate) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        gate.Record(request);
        return base.SendAsync(request, cancellationToken);
    }
}
