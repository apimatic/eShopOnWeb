using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Ambient state for one logical SDK call, shared with <see cref="MaxioCallScopeHandler"/> so the handler
/// can enforce write-once semantics and report back what the wire actually did.
/// </summary>
/// <remarks>
/// <para>
/// The scope has to live outside the <see cref="System.Net.Http.HttpRequestMessage"/>: the SDK's retry
/// pipeline builds a fresh request object per attempt, so anything hung off the request is gone by the time
/// a retry arrives. Retries run inside the caller's async context, so an <see cref="AsyncLocal{T}"/> opened
/// by the caller flows into the handler on every attempt.
/// </para>
/// <para>
/// The scope object is mutable and captured by reference, which is what lets the handler's observations
/// (further down the async flow) be visible to the caller that opened it.
/// </para>
/// </remarks>
internal sealed class MaxioCallScope : IDisposable
{
    private static readonly AsyncLocal<MaxioCallScope?> CurrentScope = new();

    private readonly MaxioCallScope? _previous;
    private int _authorizedSends;
    private int _lastStatusCode;

    private MaxioCallScope(bool singleSend)
    {
        SingleSend = singleSend;
        _previous = CurrentScope.Value;
    }

    public static MaxioCallScope? Current => CurrentScope.Value;

    /// <summary>Opens a scope for the current async flow. Dispose restores the previous one.</summary>
    public static MaxioCallScope Begin(bool singleSend)
    {
        var scope = new MaxioCallScope(singleSend);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>True when at most one request may leave this scope.</summary>
    public bool SingleSend { get; }

    /// <summary>
    /// Claims the single permitted send. Called <em>before</em> the request goes out, because a request that
    /// fails on its way out may still have been received.
    /// </summary>
    public bool TryAuthorizeSend() => Interlocked.Increment(ref _authorizedSends) == 1;

    /// <summary>True once a request has been released to the network.</summary>
    public bool AnySendAuthorized => Volatile.Read(ref _authorizedSends) > 0;

    public void RecordStatus(int statusCode) => Volatile.Write(ref _lastStatusCode, statusCode);

    /// <summary>
    /// The status of the last response seen on the wire in this scope, or null when none arrived. Used to
    /// tell an unreadable success body ("outcome unknown") from an unreadable error body ("we were rejected
    /// and the detail was lost") — the SDK destroys the status in the second case.
    /// </summary>
    public int? LastStatusCode
    {
        get
        {
            var status = Volatile.Read(ref _lastStatusCode);
            return status == 0 ? null : status;
        }
    }

    public void Dispose() => CurrentScope.Value = _previous;
}
