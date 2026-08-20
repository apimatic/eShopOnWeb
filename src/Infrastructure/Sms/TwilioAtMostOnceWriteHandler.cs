using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

internal sealed class TwilioAtMostOnceWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<SendGate?> Scope = new();

    public static IDisposable BeginCreateMessageScope()
    {
        var previous = Scope.Value;
        var gate = new SendGate();
        Scope.Value = gate;
        return new ScopeReset(previous);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsCreateMessage(request))
        {
            var gate = Scope.Value;
            if (gate is not null && Interlocked.Increment(ref gate.Sends) > 1)
            {
                throw new TwilioDuplicateWriteException();
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsCreateMessage(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri is null)
        {
            return false;
        }

        return request.RequestUri.AbsolutePath.EndsWith("/Messages.json", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SendGate
    {
        public int Sends;
    }

    private sealed class ScopeReset : IDisposable
    {
        private readonly SendGate? _previous;
        private bool _disposed;

        public ScopeReset(SendGate? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Scope.Value = _previous;
            _disposed = true;
        }
    }
}
