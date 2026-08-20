using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Twilio Programmable Messaging API (Messages resource). Uses Twilio:BaseUrl when set.
/// </summary>
public interface ITwilioMessagingClient
{
    Task<TwilioMessageSnapshot> CreateMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public record TwilioMessageSnapshot(
    string Sid,
    string Status,
    string? Body,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);
