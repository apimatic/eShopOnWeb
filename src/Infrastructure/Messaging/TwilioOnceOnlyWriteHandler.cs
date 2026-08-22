using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Holds send-count in AsyncLocal so a retried POST never reaches the network a second time.
/// The marker cannot live on HttpRequestMessage: the SDK builds a fresh request per attempt.
/// </summary>
internal sealed class TwilioOnceOnlyWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    internal static IDisposable BeginWrite()
    {
        var previous = Current.Value;
        Current.Value = new WriteScope();
        return new ScopeCloser(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method))
        {
            var scope = Current.Value;
            if (scope is not null)
            {
                scope.Count++;
                if (scope.Count > 1)
                {
                    throw new DuplicateWriteRefusedException();
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch || method == HttpMethod.Delete;

    private sealed class WriteScope
    {
        public int Count;
    }

    private sealed class ScopeCloser : IDisposable
    {
        private readonly WriteScope? _previous;
        private bool _disposed;

        public ScopeCloser(WriteScope? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Current.Value = _previous;
            _disposed = true;
        }
    }
}
