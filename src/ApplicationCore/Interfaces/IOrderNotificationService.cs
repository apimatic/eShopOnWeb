using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task<ContactNumberView> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken);
    Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, ShippingAddressInput? address, CancellationToken cancellationToken);
    Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken);
    Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ContactNumberView(int ContactNumberId, string PhoneNumber);
public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);
public sealed record OrderView(int OrderId, DateTimeOffset OrderDate, OrderStatus Status, decimal Total, IReadOnlyList<NotificationSummary> Notifications);
public sealed record NotificationSummary(int NotificationId, NotificationKind Kind, string Status);
public sealed record NotificationView(int NotificationId, int OrderId, NotificationKind Kind, string? Content, bool ContentRedacted, string? ProviderMessageSid, string ProviderStatus, int? ProviderErrorCode, DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor, int? ResendOfNotificationId);
public sealed record ReconciliationEntry(string ProviderMessageSid, string ProviderStatus, DateTimeOffset? ProviderDate, int? NotificationId, bool KnownToApplication);
public sealed record MissingProviderEntry(int NotificationId, string ProviderMessageSid, string LocalStatus);
public sealed record ReconciliationView(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationEntry> ProviderMessages, IReadOnlyList<MissingProviderEntry> MissingFromProvider);
