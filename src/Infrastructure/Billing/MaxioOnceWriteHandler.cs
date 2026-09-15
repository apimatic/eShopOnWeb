using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Refuses a second POST send that the SDK retry pipeline did not authorize.
/// Marker state lives in <see cref="AsyncLocal{T}"/> so it survives a fresh HttpRequestMessage per attempt.
/// </summary>
public sealed class MaxioOnceWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    public static IDisposable BeginWrite() => new WriteScope();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && Current.Value is { } scope)
        {
            if (Interlocked.Increment(ref scope.SendCount) > 1)
            {
                throw new DuplicateWriteRefusedException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope : IDisposable
    {
        public int SendCount;

        public WriteScope()
        {
            Current.Value = this;
        }

        public void Dispose()
        {
            if (ReferenceEquals(Current.Value, this))
            {
                Current.Value = null;
            }
        }
    }
}

public sealed class DuplicateWriteRefusedException : Exception
{
    public DuplicateWriteRefusedException()
        : base("A duplicate billing write was refused after the first attempt left the process.")
    {
    }
}
