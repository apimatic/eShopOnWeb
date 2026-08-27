using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public enum ResendOutcome
{
    /// <summary>A fresh message was produced (check the notification's status for the outcome).</summary>
    Sent,
    /// <summary>The idempotency key was seen before; the earlier message is returned, nothing new was sent.</summary>
    AlreadyProcessed,
    NotFound,
    /// <summary>The message content has been disposed of and can no longer be sent.</summary>
    ContentDisposed,
    /// <summary>The provider reports the original message as delivered.</summary>
    AlreadyDelivered,
    /// <summary>The shopper's contact number is no longer registered.</summary>
    ContactNumberRemoved
}

public sealed record ResendResult(ResendOutcome Outcome, OrderNotification? Notification);

public interface INotificationResendService
{
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);
}
