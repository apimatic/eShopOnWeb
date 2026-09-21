using System;

namespace MaxioAdvancedBilling.Core.Exceptions;

public sealed class SseTimeoutException : SseException
{
    public SseTimeoutException(TimeSpan idleTimeout)
        : base($"SSE stream idle timeout exceeded ({idleTimeout}).") =>
        IdleTimeout = idleTimeout;

    /// <summary>The idle window that elapsed without a frame arriving.</summary>
    public TimeSpan IdleTimeout { get; }
}
