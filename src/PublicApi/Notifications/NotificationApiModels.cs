using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class RegisterContactNumberRequest
{
    [Required, MaxLength(64)]
    public string Number { get; set; } = string.Empty;
}

public sealed record ContactNumberCreatedResponse(int ContactNumberId);
public sealed record ContactNumberDto(int ContactNumberId, string Number, DateTimeOffset CreatedAt);

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)]
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }
    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [MaxLength(180)] public string? Street { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(60)] public string? State { get; set; }
    [MaxLength(90)] public string? Country { get; set; }
    [MaxLength(18)] public string? ZipCode { get; set; }
}

public sealed record OrderCreatedResponse(int OrderId);
public sealed record OrderStateResponse(int OrderId, string Status);
public sealed record MyOrderDto(int OrderId, DateTimeOffset OrderDate, string Status,
    decimal Total, IReadOnlyList<NotificationSummaryDto> Notifications);
public sealed record NotificationSummaryDto(int NotificationId, string Kind, string Outcome,
    string? ProviderMessageId, DateTimeOffset? ScheduledFor, DateTimeOffset? SentAt,
    DateTimeOffset? ContentDisposedAt);
public sealed record NotificationDto(int NotificationId, int OrderId, string Kind, string Outcome,
    string? Content, string? ProviderMessageId, int? ProviderErrorCode,
    DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor, DateTimeOffset? SentAt,
    DateTimeOffset? ContentDisposedAt, int? OriginalNotificationId);

public sealed class ResendNotificationRequest
{
    [Required, MaxLength(200)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record NotificationCreatedResponse(int NotificationId);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);
public sealed record ReconciliationEntry(string? ProviderMessageId, int? NotificationId,
    string Presence, string? ProviderOutcome, string? ApplicationOutcome,
    DateTimeOffset? ProviderSentAt, DateTimeOffset? ApplicationCreatedAt);
