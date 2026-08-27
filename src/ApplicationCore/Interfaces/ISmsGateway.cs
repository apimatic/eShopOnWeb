using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider's SMS API. All methods throw SmsProviderException on provider
/// or transport failure; an undeliverable destination is an outcome on the returned
/// state, not an exception.
/// </summary>
public interface ISmsGateway
{
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider for delivery at a future time.</summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task<SmsMessageState> CancelScheduledAsync(string messageSid, CancellationToken ct = default);

    Task<SmsMessageState> GetStateAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Erases the message text at the provider; the record and its outcome survive.</summary>
    Task RedactBodyAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// The provider's own record of messages sent from this application's sending number,
    /// covering the whole [from, to] window (filtering applied provider-side).
    /// </summary>
    Task<ProviderMessageListResult> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public class ProviderMessageListResult
{
    public IReadOnlyList<ProviderMessageRecord> Messages { get; set; } = new List<ProviderMessageRecord>();

    /// <summary>True when a page cap stopped the listing before the provider signalled the end.</summary>
    public bool Truncated { get; set; }
}

public class SmsSendResult
{
    public string? MessageSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SmsMessageState
{
    public string? MessageSid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ProviderMessageRecord
{
    public string? MessageSid { get; set; }
    public string? To { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}
