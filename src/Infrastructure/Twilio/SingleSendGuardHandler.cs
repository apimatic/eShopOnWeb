using System;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Raised when the single-send guard blocks a re-send of an outbound message request. It deliberately
/// does NOT derive from <see cref="HttpRequestException"/> — that is the type the SDK's transport-retry
/// policy retries, so a refusal thrown as one would itself become retryable. A plain exception propagates
/// out unwrapped.
/// </summary>
public sealed class DuplicateSendBlockedException : Exception
{
    public DuplicateSendBlockedException()
        : base("A transport-level re-send of an outbound message was blocked to avoid a duplicate SMS.")
    {
    }
}

/// <summary>
/// A message-pipeline handler that enforces "at most one message reaches the provider per send". Because
/// a transport failure (connection reset, dropped socket) is retried on <em>every</em> verb regardless of
/// <c>HttpMethodsToRetry</c>, a create-message POST can otherwise be executed more than once — a duplicate,
/// paid SMS to a shopper. Within a scope opened by <see cref="BeginSingleSend"/> the first outbound request
/// is allowed and any retry beyond it is refused before it reaches the network.
///
/// The count is held in an <see cref="AsyncLocal{T}"/> scope (not on the <see cref="HttpRequestMessage"/>,
/// which is rebuilt per attempt) so it flows into the handler on every retry.
/// </summary>
public sealed class SingleSendGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<StrongBox<int>?> _scope = new();

    /// <summary>Open a scope around exactly one logical send; a second outbound request within it is blocked.</summary>
    public static IDisposable BeginSingleSend()
    {
        _scope.Value = new StrongBox<int>(0);
        return new Scope();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var box = _scope.Value;
        if (box is not null && Interlocked.Increment(ref box.Value) > 1)
        {
            throw new DuplicateSendBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose() => _scope.Value = null;
    }
}
