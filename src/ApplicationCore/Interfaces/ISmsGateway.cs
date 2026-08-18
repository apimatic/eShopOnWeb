using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of the SMS provider (Twilio's messaging API and phone-number lookup).
/// Implementations translate every provider outcome into these plain results or an
/// <see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.SmsGatewayException"/>, so nothing
/// provider-specific leaks past this seam. No implementation logs a destination number, message
/// body, or credential.
/// </summary>
public interface ISmsGateway
{
    /// <summary>The application's own configured sending number (Twilio:FromNumber), in E.164.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, its canonical E.164 form.
    /// A definitively invalid number returns <c>IsValid == false</c>; a provider outage throws.
    /// </summary>
    Task<PhoneLookupResult> LookupNumberAsync(string rawPhoneNumber, CancellationToken ct);

    /// <summary>Send an SMS immediately from the application's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>
    /// Queue an SMS with the provider to be sent at <paramref name="sendAt"/>. The provider holds it;
    /// this application runs no timer of its own.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Call off a scheduled message before it goes out.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>Read the provider's current delivery outcome for a message.</summary>
    Task<SmsStatusResult> FetchStatusAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's content at the provider so its text is no longer retrievable there,
    /// while the record that a message was sent and its outcome survive.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// The provider's own record of the messages it holds for the application's configured sending
    /// number over a date range, asked of the provider (filtered provider-side by that number), not
    /// filtered locally after the fact. Covers the whole range.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Outcome of a provider phone-number lookup.</summary>
public record PhoneLookupResult(bool IsValid, string? CanonicalE164);

/// <summary>Outcome of a create/schedule/cancel that the provider accepted.</summary>
public record SmsSendResult(string? ProviderMessageSid, string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's current delivery outcome for a message.</summary>
public record SmsStatusResult(string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>A message as the provider knows it, used to reconcile against local records.</summary>
public record ProviderMessage(string Sid, string? Status, string? From, string? To, DateTimeOffset? DateSent);
