using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider's messaging + lookup APIs. Everything eShop needs to know
/// about a message is obtained by asking the provider (there is no inbound webhook available).
/// Implementations never log the shopper's number and never expose the auth token.
/// </summary>
public interface ISmsProvider
{
    /// <summary>The application's own configured sending number (Twilio:FromNumber), in E.164.</summary>
    string SenderNumber { get; }

    /// <summary>
    /// Validate a number and return the provider's canonical form. Used at registration time so an
    /// unusable destination is rejected up front rather than when a later send fails.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a message with the provider. When <paramref name="sendAt"/> is supplied the message is
    /// scheduled with the provider for that time (not held by this application). Returns the provider's
    /// SID and initial status, or a non-accepted result carrying the error when the provider refused it.
    /// </summary>
    Task<SmsSendResult> SendAsync(string toPhoneNumber, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);

    /// <summary>Read the current delivery outcome of a message back from the provider by SID.</summary>
    Task<SmsMessageStatus> FetchStatusAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent (scheduled) message at the provider so it never goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's content at the provider (redact the body) so the text is no longer
    /// retrievable there, while the message record and its outcome survive.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from <see cref="SenderNumber"/> in the given
    /// range, following pagination to cover the whole range. The From filter is applied by the provider.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListOutboundMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of a phone-number validation/lookup.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber, string? NationalFormat, IReadOnlyList<string> ValidationErrors);

/// <summary>Outcome of creating a message: the SID and initial status, or the provider error.</summary>
public record SmsSendResult(string? MessageSid, string Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? ScheduledSendAt)
{
    /// <summary>True when the provider accepted the message and returned a SID.</summary>
    public bool Accepted => !string.IsNullOrEmpty(MessageSid);
}

/// <summary>A later read of a message's delivery outcome.</summary>
public record SmsMessageStatus(string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's own record of one message, as returned by the list/reconciliation call.</summary>
public record ProviderMessage(string Sid, string? From, string? To, string Status, string? Direction, int? ErrorCode, DateTimeOffset? DateSent);
