using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of registering a contact number: the stored number, or the provider's reasons for rejecting it.</summary>
public record RegisterContactNumberResult(ContactNumber? ContactNumber, IReadOnlyList<string> ValidationErrors)
{
    public bool Succeeded => ContactNumber is not null;

    public static RegisterContactNumberResult Ok(ContactNumber number) => new(number, System.Array.Empty<string>());
    public static RegisterContactNumberResult Rejected(IReadOnlyList<string> errors) => new(null, errors);
}

/// <summary>Manages a shopper's own registered contact numbers.</summary>
public interface IContactNumberService
{
    Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>Removes a number the caller owns. Returns false if the caller has no such number.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken);
}

/// <summary>A single item line for placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places orders and moves them through their lifecycle, sending the shopper an SMS at each step.
/// A message that cannot be sent never fails the underlying operation.
/// </summary>
public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(string ownerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken);

    /// <summary>Operator action: mark dispatched, tell the shopper, and queue a delivery follow-up for a few days later.</summary>
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator action: cancel the order, tell the shopper, and call off any follow-up before it goes out.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>The caller's own orders (with items), each ready to report where its notifications got to.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>The caller's own order by id, or null if it is not theirs / does not exist.</summary>
    Task<Order?> GetOwnedOrderAsync(string ownerId, int orderId, CancellationToken cancellationToken);

    /// <summary>Notifications for a set of orders, with delivery outcomes refreshed from the provider.</summary>
    Task<IReadOnlyList<Notification>> GetNotificationsForOrdersAsync(IReadOnlyList<int> orderIds, CancellationToken cancellationToken);
}

/// <summary>Outcome of an operator resend: the message it points at, and whether it reused an earlier attempt.</summary>
public record ResendResult(Notification Notification, bool Reused);

/// <summary>One row of the reconciliation report.</summary>
public record ReconciliationEntry(string ProviderSid, string? ProviderStatus, int? NotificationId, DateTimeOffset? DateSent);

/// <summary>
/// The provider's own record of messages for a date range, lined up against what eShop believes it
/// sent, so a message on one side but not the other is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>Operator actions over individual messages: resend, content disposal, and reconciliation.</summary>
public interface INotificationOperationsService
{
    /// <summary>Re-sends a message that did not reach the shopper. Repeats under the same key do not send again.</summary>
    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Disposes of a message's content at the provider and locally, keeping the record of what became of it.</summary>
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
