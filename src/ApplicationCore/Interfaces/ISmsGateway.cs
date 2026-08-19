using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Everything the SMS integration needs from the messaging provider. The application talks to
/// the provider only through this abstraction; the concrete implementation (Twilio) lives in
/// Infrastructure.
/// </summary>
public interface ISmsGateway
{
    /// <summary>The application's own configured sending number (Twilio:FromNumber).</summary>
    string FromNumber { get; }

    /// <summary>
    /// Validate a number and return its canonical E.164 form. A number the provider does not
    /// consider a usable destination comes back with <see cref="PhoneLookupResult.IsValid"/> false.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken);

    /// <summary>Send a message now. Returns the provider's identifier and initial status.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/>. The provider
    /// holds it; nothing is scheduled inside this application.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    /// <summary>Read the provider's current record for a message.</summary>
    Task<SmsSendResult?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>Cancel a message the provider has scheduled but not yet sent.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>
    /// Redact a message's body at the provider so its text is no longer retrievable there,
    /// while the message record and its delivery outcome survive.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken);

    /// <summary>
    /// List the provider's own record of messages sent from <paramref name="fromNumber"/> within
    /// the date range. The provider is asked for that sending number's messages directly.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>Outcome of a phone-number lookup.</summary>
public record PhoneLookupResult(bool IsValid, string? PhoneNumberE164);

/// <summary>Result of creating or reading a message at the provider.</summary>
public record SmsSendResult(string Sid, string Status, int? ErrorCode);

/// <summary>A message as the provider records it, used for reconciliation.</summary>
public record ProviderMessage(string Sid, string? To, string? From, string Status, int? ErrorCode, DateTimeOffset? DateSent);
