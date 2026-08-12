using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// The outcome of asking the provider to send (or schedule) a message: the provider's identifier
/// for the message and the status it had reached at that instant. Note the provider accepting a
/// message (a 201) is not a delivery confirmation — <see cref="Status"/> is a snapshot of an
/// asynchronous pipeline and is re-read later to learn the real outcome.
/// </summary>
public record MessageDispatchResult
{
    /// <summary>The provider's message identifier (e.g. an <c>SM…</c> SID), or null if none was issued.</summary>
    public string? Sid { get; init; }

    /// <summary>The provider's status at acceptance time (e.g. <c>queued</c>, <c>accepted</c>, <c>scheduled</c>).</summary>
    public required string Status { get; init; }

    public int? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>The time a scheduled message is timed to go out, when applicable.</summary>
    public DateTimeOffset? ScheduledFor { get; init; }
}
