using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Refuses a repeat HTTP send of a non-idempotent request inside an armed
/// <see cref="MaxioWriteOnceScope"/>. The Maxio .NET SDK retries transport failures on every verb
/// (POST included), so a single logical Subscribe could otherwise reach the provider more than once.
/// </summary>
internal sealed class MaxioWriteOnceRefusedException : Exception
{
    public MaxioWriteOnceRefusedException()
        : base("A repeated send of a guarded write was refused because it was already sent.")
    {
    }
}

internal sealed class MaxioWriteOnceState
{
    private readonly HashSet<string> _sent = new(StringComparer.Ordinal);

    public bool TryRecord(HttpRequestMessage request)
    {
        var key = request.Method + " " + (request.RequestUri?.ToString() ?? string.Empty);
        return _sent.Add(key);
    }
}

/// <summary>
/// Scopes "at most one network send of a guarded write" to a logical operation. The value flows
/// through async awaits, so the SDK's retry attempts for a single call observe the same state.
/// </summary>
internal static class MaxioWriteOnceScope
{
    private static readonly AsyncLocal<MaxioWriteOnceState?> _current = new();

    internal static MaxioWriteOnceState? Current => _current.Value;

    public static IDisposable Enter()
    {
        var previous = _current.Value;
        _current.Value = new MaxioWriteOnceState();
        return new ExitScope(previous);
    }

    private sealed class ExitScope : IDisposable
    {
        private readonly MaxioWriteOnceState? _previous;
        private bool _disposed;

        public ExitScope(MaxioWriteOnceState? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _current.Value = _previous;
            _disposed = true;
        }
    }
}

/// <summary>
/// Delegating handler that enforces the write-once guard. It only acts when a scope is armed
/// (i.e. only around the subscription-create call) and only for non-idempotent verbs, so GET retries
/// are unaffected. Counting happens before the request is sent.
/// </summary>
public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = MaxioWriteOnceScope.Current;
        if (scope is not null && IsWrite(request) && !scope.TryRecord(request))
        {
            throw new MaxioWriteOnceRefusedException();
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsWrite(HttpRequestMessage request) =>
        request.Method != HttpMethod.Get && request.Method != HttpMethod.Head;
}
