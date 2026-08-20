using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioWriteScopeAccessor
{
    private readonly AsyncLocal<WriteScope?> _current = new();

    public IDisposable Begin()
    {
        if (_current.Value is not null)
        {
            throw new InvalidOperationException("A Maxio write scope is already active.");
        }

        var scope = new WriteScope(this);
        _current.Value = scope;
        return scope;
    }

    public bool TryRecordSend()
    {
        var scope = _current.Value;
        return scope is null || Interlocked.Increment(ref scope.SendCount) == 1;
    }

    private sealed class WriteScope(MaxioWriteScopeAccessor owner) : IDisposable
    {
        public int SendCount;

        public void Dispose()
        {
            if (ReferenceEquals(owner._current.Value, this))
            {
                owner._current.Value = null;
            }
        }
    }
}

internal sealed class MaxioWriteReplayPreventedException : Exception
{
    public MaxioWriteReplayPreventedException()
        : base("A repeated Maxio write attempt was blocked because the first attempt has an unknown outcome.")
    {
    }
}

internal sealed class MaxioWriteOnceHandler(MaxioWriteScopeAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !accessor.TryRecordSend())
        {
            throw new MaxioWriteReplayPreventedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
