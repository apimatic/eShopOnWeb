using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A hand-written client for Twilio's Messages API (Twilio's <c>api_v2010</c> OpenAPI
/// document is the authoritative contract). Every call targets the messaging base URL
/// (<c>Twilio:BaseUrl</c> override, else <c>https://api.twilio.com</c>) and authenticates
/// with HTTP Basic AccountSid:AuthToken.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>
    /// Creates (or schedules) a message.
    /// <c>POST /2010-04-01/Accounts/{AccountSid}/Messages.json</c>.
    /// </summary>
    Task<TwilioMessage> SendMessageAsync(SendMessageCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the current state of a message so its delivery outcome can be read back
    /// (there is no public callback URL for this app).
    /// <c>GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json</c>.
    /// </summary>
    Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a not-yet-sent (scheduled) message.
    /// <c>POST .../Messages/{Sid}.json</c> with <c>Status=canceled</c>.
    /// </summary>
    Task<TwilioMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts a message's body at the provider so its text is no longer retrievable,
    /// while the message record (and its outcome) survives.
    /// <c>POST .../Messages/{Sid}.json</c> with <c>Body=''</c>.
    /// </summary>
    Task<TwilioMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages matching the filter, following
    /// pagination to cover the whole result set.
    /// <c>GET /2010-04-01/Accounts/{AccountSid}/Messages.json</c>.
    /// </summary>
    Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(TwilioMessageListQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Instruction to create or schedule an outbound message.</summary>
public record SendMessageCommand
{
    public required string To { get; init; }
    public required string Body { get; init; }

    /// <summary>Explicit sender number (E.164). Omitted for scheduled sends that use a Messaging Service.</summary>
    public string? From { get; init; }

    /// <summary>Messaging Service SID. Required when scheduling.</summary>
    public string? MessagingServiceSid { get; init; }

    /// <summary>Set to <c>fixed</c> to schedule the message for <see cref="SendAt"/>.</summary>
    public string? ScheduleType { get; init; }

    /// <summary>When the provider should send a scheduled message.</summary>
    public DateTimeOffset? SendAt { get; init; }
}

/// <summary>A projection of Twilio's <c>api.v2010.account.message</c> resource.</summary>
public record TwilioMessage
{
    public string? Sid { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? To { get; init; }
    public string? From { get; init; }
    public string? Body { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? MessagingServiceSid { get; init; }
    public string? Direction { get; init; }
    public string? Price { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateUpdated { get; init; }
}

/// <summary>Filter for <see cref="ITwilioMessagingClient.ListMessagesAsync"/>.</summary>
public record TwilioMessageListQuery
{
    /// <summary>Filter by sender number (asks the provider for that number's messages).</summary>
    public string? From { get; init; }
    public string? To { get; init; }

    /// <summary>Lower bound (inclusive) on the message's sent date.</summary>
    public DateTimeOffset? DateSentAfter { get; init; }

    /// <summary>Upper bound (inclusive) on the message's sent date.</summary>
    public DateTimeOffset? DateSentBefore { get; init; }

    public int PageSize { get; init; } = 1000;
}
