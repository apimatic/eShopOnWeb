using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider. Implementations must never throw for a message the
/// provider rejects at send time; that outcome is reported in the result.
/// </summary>
public interface ISmsService
{
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider for delivery at a future time.</summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Current provider-owned state of a message. Null if the provider no longer knows it.</summary>
    Task<SmsMessageState?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a message the provider is holding for future delivery. False if it could not be cancelled.</summary>
    Task<bool> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Erase the message body at the provider. False if the provider refused.</summary>
    Task<bool> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages sent from this application's configured
    /// sending number, across the whole of the given UTC range.
    /// </summary>
    Task<IReadOnlyList<SmsMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
