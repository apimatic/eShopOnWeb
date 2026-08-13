using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The outbound port to the SMS provider. Everything the application needs from the provider is
/// expressed here in the application's own terms; the concrete implementation is the only place that
/// knows about the provider's SDK. All methods talk to the provider's messaging API.
/// </summary>
public interface ISmsGateway
{
    /// <summary>Look a raw number up with the provider and, if it is a usable destination, return its canonical E.164 form.</summary>
    Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message now. Returns the provider's message id and current status.</summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at <paramref name="sendAt"/>. Returns the provider's message id and status.</summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Call off a message that the provider has scheduled but not yet sent.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current delivery outcome for a message.</summary>
    Task<string> GetStatusAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's body on the provider's side so its text is no longer retrievable there.</summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages this application sent (i.e. from the configured sending number)
    /// within a date range, asking the provider to filter by that number rather than filtering a wider answer here.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a phone-number lookup: whether the provider considers it a usable destination, and its canonical form.</summary>
public record PhoneLookupResult(bool IsValid, string? CanonicalNumber);

/// <summary>Result of handing a message to the provider.</summary>
public record SmsSendResult(string Sid, string Status);

/// <summary>The provider's own view of a single message, used for reconciliation.</summary>
public record ProviderMessage(string Sid, string? From, string Status, DateTimeOffset? DateSent);
