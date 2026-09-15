using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic gateway for the SMS provider (Twilio). The concrete implementation lives in
/// Infrastructure and is built directly against the provider's OpenAPI contract. The application
/// layer depends only on this abstraction.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Validate a number and return the provider's canonical form. Used at registration time so an
    /// unusable destination is rejected up front rather than when a message later fails to go out.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hand a message to the provider. When <see cref="SmsMessageRequest.SendAt"/> is set the message
    /// is queued with the provider for that time rather than sent immediately.
    /// </summary>
    Task<SmsMessageState> SendAsync(SmsMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current view of a message (its delivery outcome).</summary>
    Task<SmsMessageState> GetMessageStateAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>Call off a not-yet-sent (scheduled) message so it never reaches the recipient.</summary>
    Task<SmsMessageState> CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's text at the provider (redact the body) while leaving the record —
    /// that a message was sent and what became of it — intact.
    /// </summary>
    Task RedactContentAsync(string providerMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of outbound messages sent from this application's configured sending
    /// number over a date range. The implementation asks the provider for that number's messages
    /// directly rather than filtering a wider answer after the fact. Covers the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListOutboundMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of validating a phone number with the provider.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

/// <summary>A request to send (or schedule) one SMS.</summary>
public record SmsMessageRequest(string To, string Body, DateTimeOffset? SendAt = null);

/// <summary>The provider-owned state of a message: its identifier and current delivery outcome.</summary>
public record SmsMessageState(
    string ProviderMessageId,
    NotificationDeliveryStatus Status,
    string? ProviderStatusRaw,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? SentAt);

/// <summary>One entry from the provider's own list of outbound messages (used for reconciliation).</summary>
public record ProviderMessageRecord(
    string Sid,
    string? RawStatus,
    NotificationDeliveryStatus Status,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);
