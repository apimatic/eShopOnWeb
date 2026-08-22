using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Blocks SDK transport retries from issuing a second POST. State lives in AsyncLocal
/// so it survives a retry's fresh HttpRequestMessage.
/// </summary>
public sealed class OnceOnlyWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteGate?> Gate = new();

    public static IDisposable BeginWrite() => new WriteScope();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Patch || request.Method == HttpMethod.Put)
        {
            var gate = Gate.Value;
            if (gate is not null)
            {
                if (Interlocked.Increment(ref gate.Count) > 1)
                {
                    throw new DuplicateProviderWriteException();
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteGate
    {
        public int Count;
    }

    private sealed class WriteScope : IDisposable
    {
        public WriteScope()
        {
            Gate.Value = new WriteGate();
        }

        public void Dispose()
        {
            Gate.Value = null;
        }
    }
}

public sealed class DuplicateProviderWriteException : Exception
{
    public DuplicateProviderWriteException()
        : base("A duplicate write to the messaging provider was blocked.")
    {
    }
}
