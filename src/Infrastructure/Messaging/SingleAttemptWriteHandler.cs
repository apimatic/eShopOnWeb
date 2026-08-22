using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class SingleAttemptWriteScope : IDisposable
{
    private static readonly AsyncLocal<Counter?> Current = new();
    private readonly Counter? _previous;

    private SingleAttemptWriteScope()
    {
        _previous = Current.Value;
        Current.Value = new Counter();
    }

    public static SingleAttemptWriteScope Begin() => new();

    public void Dispose()
    {
        Current.Value = _previous;
    }

    internal static void CountWriteOrThrow()
    {
        var counter = Current.Value;
        if (counter is null)
        {
            return;
        }

        counter.Count++;
        if (counter.Count > 1)
        {
            throw new DuplicateProviderWriteException();
        }
    }

    private sealed class Counter
    {
        public int Count;
    }
}

internal sealed class SingleAttemptWriteHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post)
        {
            SingleAttemptWriteScope.CountWriteOrThrow();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
