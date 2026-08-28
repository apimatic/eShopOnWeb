using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioWriteGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable BeginWrite()
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new WriteScope(previous);
        return CurrentScope.Value;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && Interlocked.Increment(ref scope.Attempts) > 1)
        {
            throw new TwilioWriteOutcomeUnknownException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope : IDisposable
    {
        private readonly WriteScope? _previous;
        private bool _disposed;

        public WriteScope(WriteScope? previous) => _previous = previous;
        public int Attempts;

        public void Dispose()
        {
            if (_disposed) return;
            CurrentScope.Value = _previous;
            _disposed = true;
        }
    }
}

public sealed class TwilioWriteOutcomeUnknownException : Exception
{
    public TwilioWriteOutcomeUnknownException()
        : base("A retry of a provider write was blocked because the first attempt may have succeeded.") { }
}
