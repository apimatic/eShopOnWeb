using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider's messaging API. All sends originate from the application's
/// configured sending number; scheduled sends go through the configured messaging service.
/// </summary>
public interface INotificationGateway
{
    /// <summary>Send a message immediately from the configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider for delivery at a future time.</summary>
    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancel a provider-scheduled message that has not yet been sent.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current state of a single message.</summary>
    Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages sent from this application's configured
    /// sending number within a date range (provider-side filtered).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's text at the provider so it is no longer retrievable there.
    /// The message record (identifier, status, dates) survives.
    /// </summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
}
