using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.TwilioIntegration;

internal sealed class TwilioDuplicateWriteException : Exception
{
    public TwilioDuplicateWriteException()
        : base("A duplicate write to the messaging provider was refused.")
    {
    }
}

internal sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<Counter?> Gate = new();

    private sealed class Counter
    {
        public int Posts;
    }

    public static IDisposable BeginWrite() => new Scope();

    private sealed class Scope : IDisposable
    {
        public Scope()
        {
            Gate.Value = new Counter();
        }

        public void Dispose()
        {
            Gate.Value = null;
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Delete)
        {
            var gate = Gate.Value;
            if (gate is not null && Interlocked.Increment(ref gate.Posts) > 1)
            {
                throw new TwilioDuplicateWriteException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
