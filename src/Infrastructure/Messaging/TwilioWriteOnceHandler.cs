using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Blocks transport-level retries of POST/PATCH/PUT from issuing a second write.
/// Count is stored in AsyncLocal so it survives a fresh HttpRequestMessage per attempt.
/// </summary>
internal sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    private sealed class WriteScopeState
    {
        public int SendCount;
    }

    private static readonly AsyncLocal<WriteScopeState?> Current = new();

    internal static IDisposable BeginWrite()
    {
        Current.Value = new WriteScopeState();
        return new ResetScope();
    }

    internal sealed class DuplicateWriteRefusedException : Exception
    {
        public DuplicateWriteRefusedException()
            : base("A retried write was refused so a second SMS would not be sent.")
        {
        }
    }

    private sealed class ResetScope : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = Current.Value;
        if (scope is not null && IsWrite(request.Method))
        {
            var count = Interlocked.Increment(ref scope.SendCount);
            if (count > 1)
            {
                throw new DuplicateWriteRefusedException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch;
}
