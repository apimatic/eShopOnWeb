using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Opens an at-most-once window around a single message-creating send. The marker is held in an
/// <see cref="AsyncLocal{T}"/> that outlives the per-attempt <c>HttpRequestMessage</c>, so it flows into the
/// <see cref="MessageSendGuardHandler"/> on every transport retry within the caller's async context.
/// </summary>
internal sealed class SendGuardScope : IDisposable
{
    private static readonly AsyncLocal<StrongBox<int>?> Attempts = new();

    public static bool IsActive => Attempts.Value is not null;

    public SendGuardScope() => Attempts.Value = new StrongBox<int>(0);

    /// <summary>Records and returns the outbound attempt number within the active scope (1 = first send).</summary>
    public static int NextAttempt()
        => Attempts.Value is { } box ? Interlocked.Increment(ref box.Value) : 0;

    public void Dispose() => Attempts.Value = null;
}
