using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider's SMS capability, expressed in terms the application needs: send a
/// message (now or scheduled), read back a message's delivery state, call off a scheduled message,
/// dispose of a message's text at the provider, and list the messages the provider recorded as
/// sent from this application's own configured sending number.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Sends a message. When <see cref="SmsSendRequest.SendAt"/> is set the message is scheduled with the provider.</summary>
    Task<SmsMessage> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a message by its identifier.</summary>
    Task<SmsMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Calls off a message that is still scheduled and has not yet gone out.</summary>
    Task CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message's text at the provider so it is no longer retrievable there, while the record survives.</summary>
    Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the messages the provider recorded as sent from this application's configured sending
    /// number within the given range. The sender filter is applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<SmsMessage>> ListOutboundFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A request to send one SMS.</summary>
public class SmsSendRequest
{
    public SmsSendRequest(string to, string body, DateTimeOffset? sendAt = null)
    {
        To = to;
        Body = body;
        SendAt = sendAt;
    }

    public string To { get; }
    public string Body { get; }

    /// <summary>When set, the provider is asked to schedule the message for this instant instead of sending it immediately.</summary>
    public DateTimeOffset? SendAt { get; }
}

/// <summary>The provider's view of a message — everything the application stores or reconciles against.</summary>
public class SmsMessage
{
    public string Sid { get; init; } = string.Empty;
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public string? Body { get; init; }
}
