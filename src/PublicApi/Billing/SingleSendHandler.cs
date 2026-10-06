using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Refuses unauthorised re-sends of a non-idempotent write. The SDK's retry
/// pipeline retries transport failures on every verb — including POST — and
/// re-captures the execution context per attempt, so per-attempt AsyncLocal
/// writes are lost. The scope therefore carries a <em>mutable</em> counter
/// object through the AsyncLocal flow: the object reference survives the
/// context re-capture, so mutations made by the handler persist across retries.
/// A write wrapped in <see cref="BeginSingleSendScope"/> is sent at most once
/// per logical call; a retry attempt throws <see cref="DuplicateSendException"/>,
/// which the caller reconciles by re-reading provider state (the outcome of the
/// single allowed send is unknown, not necessarily a failure).
/// </summary>
public sealed class SingleSendHandler : DelegatingHandler
{
    private sealed class SendScope
    {
        public int Sends;
    }

    private static readonly AsyncLocal<SendScope?> Current = new();

    /// <summary>
    /// Opens a scope in which the outgoing request may be sent exactly once.
    /// The scope must wrap a single logical write call.
    /// </summary>
    public static IDisposable BeginSingleSendScope() => new SingleSendScope();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = Current.Value;
        if (scope is not null)
        {
            if (scope.Sends >= 1)
            {
                throw new DuplicateSendException(
                    "A retry of this write was refused by the single-send guard; the provider may have already received it.");
            }
            scope.Sends++;
        }
        return base.SendAsync(request, cancellationToken);
    }

    private sealed class SingleSendScope : IDisposable
    {
        private readonly SendScope? _previous = Current.Value;
        private readonly SendScope _scope = new();

        public SingleSendScope() => Current.Value = _scope;

        public void Dispose() => Current.Value = _previous;
    }
}

/// <summary>
/// Private sentinel: deliberately does not derive from <see cref="HttpRequestException"/>,
/// which is the type the SDK's retry pipeline retries.
/// </summary>
internal sealed class DuplicateSendException : Exception
{
    public DuplicateSendException(string message) : base(message)
    {
    }
}