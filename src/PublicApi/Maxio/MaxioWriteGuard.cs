using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Guarantees that a single Maxio write (POST/PUT/PATCH/DELETE) is transmitted over the wire at most
/// once. The APIMatic SDK retries transport-level failures on every verb, and a retry can re-send a
/// write whose first attempt already reached the server. The caller opens a
/// <see cref="MaxioWriteGuardScope"/> around a logical write; because the marker lives in an
/// <see cref="AsyncLocal{T}"/> value it flows into this handler on every retry attempt of that write,
/// and any re-send after the first is refused with <see cref="MaxioWriteResendRefusedException"/>
/// (which is not an <see cref="HttpRequestException"/>, so the SDK's retry pipeline does not retry it).
/// </summary>
public sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    private readonly AsyncLocal<MaxioWriteGuardScope?> _currentScope = new();

    public MaxioWriteGuardScope OpenScope()
    {
        var scope = new MaxioWriteGuardScope(this);
        _currentScope.Value = scope;
        return scope;
    }

    internal void CloseScope(MaxioWriteGuardScope scope)
    {
        if (ReferenceEquals(_currentScope.Value, scope))
        {
            _currentScope.Value = null;
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method))
        {
            var scope = _currentScope.Value;
            if (scope is not null)
            {
                if (scope.AttemptSent)
                {
                    throw new MaxioWriteResendRefusedException();
                }

                scope.AttemptSent = true;
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post ||
        method == HttpMethod.Put ||
        method == HttpMethod.Patch ||
        method == HttpMethod.Delete;
}

/// <summary>
/// Marks the logical write whose resends <see cref="MaxioWriteGuardHandler"/> must refuse. Dispose the
/// scope when the write has been attempted so a later, unrelated write in the same call is not blocked.
/// </summary>
public sealed class MaxioWriteGuardScope : IDisposable
{
    private readonly MaxioWriteGuardHandler _handler;

    internal MaxioWriteGuardScope(MaxioWriteGuardHandler handler)
    {
        _handler = handler;
    }

    internal bool AttemptSent { get; set; }

    public void Dispose() => _handler.CloseScope(this);
}

/// <summary>
/// Thrown by <see cref="MaxioWriteGuardHandler"/> when a write is re-sent after its first attempt.
/// The write's outcome is unknown and must be settled by re-reading provider state.
/// </summary>
public sealed class MaxioWriteResendRefusedException : Exception
{
    public MaxioWriteResendRefusedException()
        : base("A Maxio write was retried after it had already been sent once; its outcome is unknown.")
    {
    }
}
