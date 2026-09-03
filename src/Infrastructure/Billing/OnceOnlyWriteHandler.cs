using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Blocks SDK transport retries from re-sending a POST. The "already sent" marker lives in
/// AsyncLocal so it survives the fresh HttpRequestMessage created on each Polly attempt.
/// </summary>
internal sealed class OnceOnlyWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    public static IDisposable BeginWrite()
    {
        var scope = new WriteScope();
        Current.Value = scope;
        return scope;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && Current.Value is { } scope)
        {
            if (Interlocked.Increment(ref scope.Sends) > 1)
            {
                throw new DuplicateWriteRefusedException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope : IDisposable
    {
        public int Sends;

        public void Dispose()
        {
            if (ReferenceEquals(Current.Value, this))
            {
                Current.Value = null;
            }
        }
    }
}
