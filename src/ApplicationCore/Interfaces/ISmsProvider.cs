using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio) contract used by the notification flows.
/// Implementations are built against the provider's OpenAPI specification.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns the
    /// provider's canonical form of the number when it is.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message for immediate delivery.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider for delivery at a future time.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a message.</summary>
    Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message the provider has not sent yet.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so it is no longer retrievable there.</summary>
    Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's sending
    /// number within a date range (inclusive), paging through the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public string? Error { get; set; }
}

public class ProviderMessage
{
    public string Sid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
