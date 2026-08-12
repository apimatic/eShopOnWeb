using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;

/// <summary>
/// A message resource as the provider reports it — the shape returned by a send, a fetch, or a
/// reconciliation list. Field names mirror the provider's message resource.
/// </summary>
public class ProviderMessage
{
    /// <summary>The provider's unique message identifier (e.g. an <c>SM…</c> SID).</summary>
    public string Sid { get; init; } = string.Empty;

    /// <summary>The provider's current status (queued, sending, sent, delivered, undelivered, failed, scheduled, canceled, accepted).</summary>
    public string Status { get; init; } = string.Empty;

    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Sender the message went out from (the account's configured sending number for this app's traffic).</summary>
    public string? From { get; init; }

    /// <summary>Destination the message was addressed to (PII — never written to logs).</summary>
    public string? To { get; init; }

    /// <summary>When the provider sent the message, if it has been sent.</summary>
    public DateTimeOffset? DateSent { get; init; }
}
