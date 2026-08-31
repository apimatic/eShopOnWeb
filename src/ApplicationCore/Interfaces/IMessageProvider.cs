using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio) as the application sees it. Implementations must
/// never leak secrets or destination numbers into exception messages.
/// </summary>
public interface IMessageProvider
{
    /// <summary>
    /// Asks the provider whether the given number is a usable destination and, if so,
    /// returns the provider's canonical form of it.
    /// </summary>
    Task<ProviderValidatedNumber> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct = default);

    /// <summary>Queues a message with the provider itself for delivery at <paramref name="sendAt"/>.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Cancels a provider-queued (scheduled, not yet sent) message.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current record of a message.</summary>
    Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's text at the provider. The message record (and its outcome)
    /// survives; the body no longer does.
    /// </summary>
    Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number whose sent date falls in [from, to]. Covers the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public class ProviderValidatedNumber
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
}

public class ProviderMessage
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}
