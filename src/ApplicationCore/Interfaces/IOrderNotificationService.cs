using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactNumberResult> RegisterContactAsync(string buyerId, string input, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactNumberResult>> GetContactsAsync(string buyerId, CancellationToken cancellationToken);
    Task<bool> RemoveContactAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
    Task<int> PlaceOrderAsync(string buyerId, PlaceOrderCommand command, CancellationToken cancellationToken);
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderSummaryResult>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationResult>?> GetNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken);
    Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ContactNumberResult(int ContactNumberId, string Number, DateTimeOffset CreatedAt);
public sealed record PlaceOrderLine(int CatalogItemId, int Quantity);
public sealed record ShippingAddressCommand(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderCommand(IReadOnlyList<PlaceOrderLine> Items, ShippingAddressCommand ShippingAddress);
public sealed record NotificationResult(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    string Status,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    string? ProviderErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ContentDisposedAt,
    int? ResendOfNotificationId);
public sealed record OrderSummaryResult(
    int OrderId,
    string Status,
    DateTimeOffset OrderedAt,
    decimal Total,
    IReadOnlyList<NotificationResult> Notifications);
public sealed record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    string Match,
    string? ProviderStatus,
    string? LocalStatus,
    string? ProviderDateSent);
public sealed record ReconciliationResult(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationEntry> Entries);
