using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactRegistrationResult> RegisterContactAsync(string buyerId, string input, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactNumber>> ListContactsAsync(string buyerId, CancellationToken cancellationToken);
    Task<ContactDeletionResult> DeleteContactAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, string buyerId, CancellationToken cancellationToken);
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<ContentDisposalResult> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<NotificationReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
    Task RetryPendingCancellationsAsync(CancellationToken cancellationToken);
}

public sealed record ContactRegistrationResult(ContactRegistrationOutcome Outcome, ContactNumber? ContactNumber, string? Error);
public enum ContactRegistrationOutcome { Created, Duplicate, Invalid, ProviderUnavailable }

public sealed record ContactDeletionResult(ContactDeletionOutcome Outcome);
public enum ContactDeletionOutcome { Deleted, NotFound, ProviderUnavailable }

public sealed record ResendNotificationResult(ResendNotificationOutcome Outcome, OrderNotification? Notification);
public enum ResendNotificationOutcome { Created, Existing, NotFound, NotEligible, ContactRemoved, ContentDisposed }

public sealed record ContentDisposalResult(ContentDisposalOutcome Outcome);
public enum ContentDisposalOutcome { Disposed, AlreadyDisposed, NotFound, ProviderUnavailable, NotProviderBacked }

public sealed record NotificationReconciliationResult(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<NotificationReconciliationEntry> Entries);

public sealed record NotificationReconciliationEntry(
    string Source,
    string? ProviderMessageId,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    DateTimeOffset? ProviderSentAt);
