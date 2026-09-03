using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.TwilioMessaging;

internal sealed class TwilioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteOnceScope?> Current = new();

    internal static IDisposable BeginWriteScope()
    {
        var scope = new WriteOnceScope();
        Current.Value = scope;
        return new ScopeReset();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isWrite = request.Method == HttpMethod.Post
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Patch
            || request.Method == HttpMethod.Delete;

        if (isWrite && Current.Value is { } scope)
        {
            if (Interlocked.Increment(ref scope.SendCount) > 1)
            {
                throw new TwilioDuplicateWriteRefusedException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteOnceScope
    {
        public int SendCount;
    }

    private sealed class ScopeReset : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}

internal sealed class TwilioDuplicateWriteRefusedException : Exception
{
    public TwilioDuplicateWriteRefusedException()
        : base("A duplicate provider write was refused.")
    {
    }
}
