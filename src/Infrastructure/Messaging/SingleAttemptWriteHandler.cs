using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class DuplicateProviderWriteException : Exception
{
    public DuplicateProviderWriteException()
        : base("A retried write was blocked from reaching the provider.")
    {
    }
}

internal sealed class SingleAttemptWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<Counter?> CounterSlot = new();

    private sealed class Counter
    {
        public int Value;
    }

    public static IDisposable BeginScope()
    {
        CounterSlot.Value = new Counter();
        return new ScopeReset();
    }

    private sealed class ScopeReset : IDisposable
    {
        public void Dispose() => CounterSlot.Value = null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method) && CounterSlot.Value is { } counter)
        {
            if (Interlocked.Increment(ref counter.Value) > 1)
            {
                throw new DuplicateProviderWriteException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post
        || method == HttpMethod.Put
        || method == HttpMethod.Patch
        || method == HttpMethod.Delete;
}
