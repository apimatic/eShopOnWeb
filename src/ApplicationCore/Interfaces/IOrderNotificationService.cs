using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ContactNumberResult(int ContactNumberId, string PhoneNumber);

public record OrderLineResult(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record NotificationResult(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Body,
    bool ContentRedacted,
    string? ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? ScheduledSendAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProviderDateSent);

public record ShopperOrderResult(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineResult> Items,
    IReadOnlyList<NotificationResult> Notifications);

public record ResendNotificationResult(int NotificationId);

public record ReconciliationItem(
    string? ProviderMessageSid,
    int? NotificationId,
    string Match,
    string? ProviderStatus,
    string? ApplicationStatus,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ApplicationCreatedAt);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationItem> Items);

public interface IOrderNotificationService
{
    Task<ContactNumberResult> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactNumberResult>> ListContactNumbersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);

    Task<ShopperOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipTo, CancellationToken cancellationToken = default);
    Task<ShopperOrderResult?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<ShopperOrderResult?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperOrderResult>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationResult>?> GetOrderNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);

    Task<ResendNotificationResult?> ResendNotificationAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<bool?> RedactNotificationContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
