using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class RegisterContactNumberRequest
{
    public string Number { get; set; } = string.Empty;
}

public sealed record ContactNumberResponse(int ContactNumberId, string Number, DateTimeOffset CreatedAt);
public sealed record RegisterContactNumberResponse(int ContactNumberId, string Number);

public sealed class PlaceOrderRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = "Not supplied";
    public string City { get; set; } = "Not supplied";
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = "Not supplied";
    public string ZipCode { get; set; } = "Not supplied";
}

public sealed record PlaceOrderResponse(int OrderId);

public sealed record MyOrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    int NotificationCount,
    IReadOnlyDictionary<string, int> NotificationStatuses);

public sealed record NotificationResponse(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    string? ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ContentDisposedAt,
    int? ResendOfNotificationId);

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ResendNotificationResponse(int NotificationId);

public sealed record ReconciliationEntryResponse(
    string MatchStatus,
    int? NotificationId,
    int? OrderId,
    string? ProviderMessageSid,
    string? ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? ApplicationCreatedAt,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderSentAt);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int ProviderOnlyCount,
    int ApplicationOnlyCount,
    IReadOnlyList<ReconciliationEntryResponse> Entries);
