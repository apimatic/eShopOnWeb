using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider, as this application needs it: send a message now, schedule one for later,
/// read a message's current outcome, cancel a not-yet-sent one, dispose of a message's content, and
/// list the messages the provider holds for this application's own sending number.
/// The concrete implementation is built against the provider's published contract in Infrastructure.
/// </summary>
public interface ISmsGateway
{
    /// <summary>This application's own configured sending number (its provider "from" number).</summary>
    string SendingNumber { get; }

    /// <summary>Sends a message immediately.</summary>
    Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record for a message by its identifier.</summary>
    Task<SmsDispatchResult> GetStatusAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>Calls off a message the provider has not yet sent.</summary>
    Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's text at the provider while keeping the record it was sent.</summary>
    Task DisposeContentAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the messages the provider holds that were sent from this application's own configured
    /// sending number within the range, asking the provider to filter by that number rather than
    /// filtering a wider answer afterwards. Covers the whole range by following the provider's paging.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>The provider's response to handing it (or asking it about) one message.</summary>
public record SmsDispatchResult(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

/// <summary>One message as the provider's own records describe it, used for reconciliation.</summary>
public record ProviderMessage(
    string Sid,
    string? Status,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
