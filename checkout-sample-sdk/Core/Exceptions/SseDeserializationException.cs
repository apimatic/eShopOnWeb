using System;

namespace PayPalServerSdk.Core.Exceptions;

public sealed class SseDeserializationException : SseException
{
    public SseDeserializationException(string rawFrame, Exception innerException)
        : base(
            "Failed to deserialize a Server-Sent Events frame. See RawFrame for the offending payload.",
            innerException) =>
        RawFrame = rawFrame;

    /// <summary>The raw frame payload (UTF-8 decoded) that failed to deserialize.</summary>
    public string RawFrame { get; }
}
