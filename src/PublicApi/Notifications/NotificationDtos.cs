using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed record RegisterContactNumberRequest(string MobileNumber);
public sealed record RegisterContactNumberResponse(int ContactNumberId, string MobileNumber);
public sealed record ContactNumberDto(int ContactNumberId, string MobileNumber, DateTimeOffset CreatedAt);

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record PlaceOrderResponse(int OrderId);

public sealed record ResendNotificationRequest(string IdempotencyKey);
public sealed record ResendNotificationResponse(int NotificationId);

public sealed record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string Outcome,
    string? ProviderSid,
    string? ProviderStatus,
    int? ProviderErrorCode,
    string? Content,
    bool ContentDisposed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? LastRefreshedAt,
    bool Stale);

public sealed record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string Progress,
    decimal Total,
    IReadOnlyList<NotificationDto> Notifications);

public sealed record ReconciliationRow(
    string Classification,
    string? ProviderSid,
    int? NotificationId,
    string? ProviderStatus,
    string? ApplicationOutcome,
    DateTimeOffset? ProviderDateSent);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Complete,
    IReadOnlyList<ReconciliationRow> Rows);

public sealed class NotificationApiException : Exception
{
    public NotificationApiException(int statusCode, string safeMessage) : base(safeMessage)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
