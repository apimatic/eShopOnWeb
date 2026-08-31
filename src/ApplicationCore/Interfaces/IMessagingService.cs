using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of the messaging provider. Every method throws
/// <see cref="Exceptions.MessagingException"/> on provider or transport failure —
/// no SDK exception types escape this boundary.
/// </summary>
public interface IMessagingService
{
    /// <summary>Send a text message immediately.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct = default);

    /// <summary>Queue a text message with the provider for delivery at a future time.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Call off a message that is still queued with the provider (not yet sent).</summary>
    Task CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Ask the provider for a message's current delivery outcome.</summary>
    Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken ct = default);

    /// <summary>Dispose of a message's text at the provider; the record of the message survives.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default);

    /// <summary>
    /// The provider's own record of messages sent from this application's configured
    /// sending number within the given date-sent range. Pages through the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesFromSenderAsync(DateTimeOffset sentAfter, DateTimeOffset sentBefore, CancellationToken ct = default);
}

/// <summary>The provider-owned state of a single message.</summary>
public class ProviderMessage
{
    public string? Sid { get; set; }
    public string? To { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public string? Body { get; set; }
}
