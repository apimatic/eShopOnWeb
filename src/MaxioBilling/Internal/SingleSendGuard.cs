using System.Runtime.CompilerServices;

namespace Microsoft.eShopWeb.MaxioBilling.Internal;

/// <summary>
/// Thrown by <see cref="SingleSendHandler"/> when the SDK's retry pipeline tries to re-send a
/// write that has already gone out once.
/// <para>
/// It deliberately does NOT derive from <see cref="HttpRequestException"/>: that is the very type
/// the SDK retries, so a refusal expressed as one would itself be retried.
/// </para>
/// </summary>
internal sealed class WriteAlreadySentException : Exception
{
    public WriteAlreadySentException()
        : base("The billing request was already sent once; a retry was refused.")
    {
    }
}

/// <summary>
/// Marks an async region as "at most one outbound request".
/// <para>
/// The SDK retries an <see cref="HttpRequestException"/> on every verb regardless of
/// <c>HttpMethodsToRetry</c>, and a connection reset thrown after the bytes reached Maxio is
/// indistinguishable from one thrown before — so without this guard a single subscribe could
/// enroll a customer more than once.
/// </para>
/// <para>
/// The counter lives in an <see cref="AsyncLocal{T}"/> rather than on the
/// <see cref="HttpRequestMessage"/> because the SDK builds a fresh request object per attempt,
/// so a marker on the request would be gone by the retry. Retries run inside the caller's async
/// context, so the scope flows into the handler on every attempt.
/// </para>
/// </summary>
internal sealed class SingleSendScope : IDisposable
{
    private static readonly AsyncLocal<StrongBox<int>?> CurrentScope = new();

    private SingleSendScope()
    {
    }

    /// <summary>Opens a scope around a write. Dispose it once the outcome is settled.</summary>
    public static SingleSendScope Begin()
    {
        CurrentScope.Value = new StrongBox<int>(0);
        return new SingleSendScope();
    }

    /// <summary>
    /// Counts a send before it goes out. Returns false when this scope has already spent its one send.
    /// </summary>
    public static bool TryClaimSend()
    {
        var counter = CurrentScope.Value;
        if (counter is null)
        {
            // No scope open: reads and anything else outside a guarded write are unaffected.
            return true;
        }

        return Interlocked.Increment(ref counter.Value) == 1;
    }

    /// <summary>
    /// Releases the claim, so a later write in the same request is not turned away by a stale scope.
    /// </summary>
    public void Dispose() => CurrentScope.Value = null;
}

/// <summary>Enforces the at-most-one-send rule opened by <see cref="SingleSendScope"/>.</summary>
internal sealed class SingleSendHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!SingleSendScope.TryClaimSend())
        {
            throw new WriteAlreadySentException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
