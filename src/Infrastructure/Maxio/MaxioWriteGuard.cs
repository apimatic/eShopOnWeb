using System;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Guards non-idempotent Maxio writes against the SDK's transport-level retry pipeline. The SDK
/// (Polly) retries <see cref="HttpRequestException"/> on <em>every</em> verb, so a connection reset
/// thrown after the bytes reached Maxio can otherwise resend a POST and create a second
/// customer/subscription. A caller opens a single-attempt scope around one logical write; the
/// companion <see cref="MaxioSingleWriteAttemptHandler"/> then refuses any re-send within that scope.
/// </summary>
public static class MaxioWriteGuard
{
    private static readonly AsyncLocal<StrongBox<int>?> _attempts = new();

    internal static StrongBox<int>? CurrentAttempts => _attempts.Value;

    /// <summary>
    /// Begins a scope in which at most one HTTP send is allowed. The count lives in async-local
    /// state (not on the <see cref="HttpRequestMessage"/>, which is rebuilt per retry attempt), so
    /// it flows into the retry pipeline running in this async context. Dispose to end the scope.
    /// </summary>
    public static IDisposable BeginSingleAttempt()
    {
        _attempts.Value = new StrongBox<int>(0);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _attempts.Value = null;
        }
    }
}

/// <summary>
/// Thrown by <see cref="MaxioSingleWriteAttemptHandler"/> when the transport pipeline tries to
/// re-send a guarded write. It is deliberately NOT an <see cref="HttpRequestException"/> so the
/// retry pipeline does not itself retry the refusal. Its meaning is "the write may already have
/// taken effect" — the caller must reconcile provider state rather than assume failure.
/// </summary>
public sealed class MaxioWriteResentException : Exception
{
    public MaxioWriteResentException()
        : base("A billing write was resent by the transport retry pipeline and was refused to preserve idempotency.")
    {
    }
}

/// <summary>
/// Refuses a second HTTP send inside a <see cref="MaxioWriteGuard.BeginSingleAttempt"/> scope,
/// holding a guarded write to exactly one attempt on the wire.
/// </summary>
public sealed class MaxioSingleWriteAttemptHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempts = MaxioWriteGuard.CurrentAttempts;
        if (attempts is not null)
        {
            // Count the send BEFORE it goes out: a request that failed on the way out may still
            // have been received, so the first attempt must be treated as possibly-effective.
            var count = Interlocked.Increment(ref attempts.Value);
            if (count > 1)
            {
                throw new MaxioWriteResentException();
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
