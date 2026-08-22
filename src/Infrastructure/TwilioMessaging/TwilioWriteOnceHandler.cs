using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.TwilioMessaging;

internal sealed class TwilioDuplicateWriteException : Exception
{
    public TwilioDuplicateWriteException()
        : base("A duplicate Twilio write was blocked.")
    {
    }
}

internal static class TwilioWriteOnce
{
    private static readonly AsyncLocal<Counter?> State = new();

    internal sealed class Counter
    {
        public int Count;
    }

    public static IDisposable Enter()
    {
        var previous = State.Value;
        State.Value = new Counter();
        return new Restorer(previous);
    }

    public static bool TryConsumeWrite()
    {
        var current = State.Value;
        if (current is null)
        {
            return true;
        }

        if (current.Count >= 1)
        {
            return false;
        }

        current.Count++;
        return true;
    }

    private sealed class Restorer : IDisposable
    {
        private readonly Counter? _previous;

        public Restorer(Counter? previous) => _previous = previous;

        public void Dispose() => State.Value = _previous;
    }
}

internal sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isWrite = request.Method == HttpMethod.Post
                      || request.Method == HttpMethod.Put
                      || request.Method == HttpMethod.Patch
                      || request.Method == HttpMethod.Delete;

        if (isWrite && !TwilioWriteOnce.TryConsumeWrite())
        {
            throw new TwilioDuplicateWriteException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
