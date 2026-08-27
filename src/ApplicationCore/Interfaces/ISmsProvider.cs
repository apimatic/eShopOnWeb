using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio) contract: sending, scheduling, cancelling,
/// inspecting, redacting and listing SMS messages.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Send a message now, or queue it with the provider for <paramref name="sendAt"/> when set.</summary>
    Task<SmsSendResult> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record of a message. Null when the provider does not know it.</summary>
    Task<SmsMessageDetails?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message the provider has queued for later. False when it could not be cancelled.</summary>
    Task<bool> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Permanently redact the message's text at the provider, keeping the message record itself.</summary>
    Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages sent from this application's configured
    /// sending number within the given (inclusive) date range.
    /// </summary>
    Task<IReadOnlyList<SmsMessageDetails>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
