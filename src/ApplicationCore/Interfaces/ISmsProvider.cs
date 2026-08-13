using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The SMS provider (Twilio) as the rest of the app needs it: validate a destination, send and
/// schedule messages, cancel a scheduled message, read a message's outcome, dispose of a message's
/// content, and list the provider's own record of messages sent from this app's number.
/// Implementations must never write auth secrets or destination numbers to logs.
/// </summary>
public interface ISmsProvider
{
    /// <summary>This application's own configured sending number (Twilio:FromNumber), in E.164.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical E.164 form.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now. Returns the provider's identifier and initial status.</summary>
    Task<ProviderSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider for future delivery at <paramref name="sendAt"/>.</summary>
    Task<ProviderSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Calls off a message still scheduled with the provider so it never goes out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads a single message's current delivery outcome from the provider.</summary>
    Task<ProviderMessageState?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text at the provider while keeping the record that a message was sent
    /// and what became of it.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages sent from <see cref="FromNumber"/> within the range,
    /// covering the whole range (all pages). Filtering by the sending number is done by the provider.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of validating and canonicalising a phone number.</summary>
public record PhoneLookupResult(bool IsUsableDestination, string? CanonicalNumber, string? Reason);

/// <summary>Result of handing a message to the provider.</summary>
public record ProviderSendResult(string Sid, string Status, int? ErrorCode);

/// <summary>A message's current outcome as the provider sees it.</summary>
public record ProviderMessageState(string Status, int? ErrorCode, DateTimeOffset? DateSent);

/// <summary>An entry from the provider's list of messages, for reconciliation.</summary>
public record ProviderMessage(string Sid, string Status, int? ErrorCode, string From, DateTimeOffset? DateSent);
