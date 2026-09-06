using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Marks a region in which at most one HTTP request may leave the process.
/// </summary>
/// <remarks>
/// <para>
/// The Maxio SDK's retry pipeline re-sends on transport failures (a connection reset, a dropped
/// socket) regardless of the HTTP verb, and a reset thrown after the bytes reached the server is
/// indistinguishable from one thrown before. Left alone, that means a single "subscribe" click can
/// enroll a shopper twice. Retries cannot be switched off — the floor is still two attempts — so the
/// only way to hold the count at one is to refuse the re-send before it reaches the network.
/// </para>
/// <para>
/// The counter deliberately lives in an <see cref="AsyncLocal{T}"/> rather than on the
/// <see cref="System.Net.Http.HttpRequestMessage"/>: a fresh request object is built for every
/// attempt, so a marker attached to the request is gone by the time the retry runs. Retries execute
/// inside the caller's async context, so this scope flows into the handler on every attempt.
/// </para>
/// <para>
/// The scope is released on dispose, so a refusal is never sticky: the next call starts a fresh one.
/// </para>
/// </remarks>
internal sealed class SingleSendScope : IDisposable
{
    private static readonly AsyncLocal<SendCounter?> CurrentScope = new();

    private SingleSendScope()
    {
    }

    /// <summary>Opens a scope in which exactly one send is authorised.</summary>
    public static SingleSendScope Begin()
    {
        CurrentScope.Value = new SendCounter();
        return new SingleSendScope();
    }

    /// <summary>
    /// Records that a request is about to go out. Returns false when it must be refused, i.e. it is
    /// a re-send inside a scope that has already spent its single authorised send. Counting happens
    /// before the send, because a request that failed on the way out may still have been received.
    /// </summary>
    public static bool TryRegisterSend()
    {
        var counter = CurrentScope.Value;
        if (counter is null)
        {
            return true;
        }

        return Interlocked.Increment(ref counter.Sends) == 1;
    }

    public void Dispose()
    {
        CurrentScope.Value = null;
    }

    private sealed class SendCounter
    {
        public int Sends;
    }
}
