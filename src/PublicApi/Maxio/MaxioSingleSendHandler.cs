using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A DelegatingHandler that guarantees a write request is sent to the provider at most once.
/// The APIMatic SDK retries transport failures on every verb (POST included), so without a guard a
/// create that failed after reaching the provider would be re-sent. Code that needs the guarantee
/// opens a write scope with <see cref="EnterScope"/>; the first request observed inside the scope is
/// sent and any SDK retry of that request is refused with <see cref="MaxioResendRefusedException"/>.
/// The guard state lives in an AsyncLocal so it flows across the SDK's async retry pipeline but never
/// leaks to other requests or threads.
/// </summary>
public sealed class MaxioSingleSendHandler : DelegatingHandler
{
    private sealed class WriteScope
    {
        public bool RequestSent;
    }

    private static readonly AsyncLocal<WriteScope?> _scope = new();

    public static IDisposable EnterScope()
    {
        var previous = _scope.Value;
        _scope.Value = new WriteScope();
        return new ScopeReset(previous);
    }

    private sealed class ScopeReset : IDisposable
    {
        private readonly WriteScope? _previous;

        public ScopeReset(WriteScope? previous) => _previous = previous;

        public void Dispose() => _scope.Value = _previous;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = _scope.Value;
        if (scope is not null)
        {
            if (scope.RequestSent)
            {
                throw new MaxioResendRefusedException();
            }

            scope.RequestSent = true;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
