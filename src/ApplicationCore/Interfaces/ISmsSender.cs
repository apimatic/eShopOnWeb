using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider's message operations, expressed provider-neutrally: send (optionally
/// scheduled), fetch current state, cancel a not-yet-sent message, dispose of a message's content, and
/// list the provider's own record of messages for reconciliation.
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// The application's own configured sending number (the provider's <c>FromNumber</c>). Reconciliation
    /// asks the provider only for this number's messages.
    /// </summary>
    string SendingNumber { get; }

    /// <summary>Sends (or schedules) a message and returns the provider's record of it, including its identifier.</summary>
    Task<SmsMessage> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record of a message by its identifier.</summary>
    Task<SmsMessage> GetAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that has been scheduled but not yet sent.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable there, while
    /// the record that a message was sent and what became of it survives.
    /// </summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Lists the provider's own record of messages matching the filter, across the whole range.</summary>
    Task<IReadOnlyList<SmsMessage>> ListAsync(SmsListFilter filter, CancellationToken cancellationToken = default);
}
