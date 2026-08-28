using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class RegisterContactNumberRequest
{
    [Required, StringLength(64)]
    public string MobileNumber { get; set; } = string.Empty;
}

public sealed record RegisterContactNumberResponse(int ContactNumberId);

public sealed record ContactNumberDto(int ContactNumberId, string MobileNumber, DateTimeOffset RegisteredAt);

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)]
    public List<OrderLineRequest> Items { get; set; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class OrderLineRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required, StringLength(180)]
    public string Street { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string City { get; set; } = string.Empty;

    [StringLength(60)]
    public string State { get; set; } = string.Empty;

    [Required, StringLength(90)]
    public string Country { get; set; } = string.Empty;

    [Required, StringLength(18)]
    public string ZipCode { get; set; } = string.Empty;
}

public sealed record PlaceOrderResponse(int OrderId);
public sealed record ChangeOrderStateResponse(int OrderId, string Status);

public sealed record OrderLineDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record NotificationSummaryDto(
    int NotificationId,
    string Type,
    string ProviderStatus,
    bool Scheduled,
    int? ProviderErrorCode);

public sealed record MyOrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<OrderLineDto> Items,
    IReadOnlyList<NotificationSummaryDto> Notifications);

public sealed record NotificationDto(
    int NotificationId,
    int OrderId,
    string Type,
    string? Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    string? ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    string? ProviderErrorMessage,
    DateTimeOffset? ProviderDateCreated,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ContentDeletedAt,
    int? ResendOfNotificationId);

public sealed class ResendNotificationRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ResendNotificationResponse(int NotificationId);

public sealed record ReconciliationEntryDto(
    string Match,
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? ApplicationStatus,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? ApplicationCreatedAt);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int ProviderOnlyCount,
    int ApplicationOnlyCount,
    IReadOnlyList<ReconciliationEntryDto> Entries);

public enum OperationError
{
    None,
    Invalid,
    NotFound,
    Conflict,
    ProviderUnavailable
}

public sealed record OperationResult<T>(T? Value, OperationError Error = OperationError.None, string? Message = null)
{
    public bool Succeeded => Error == OperationError.None;
    public static OperationResult<T> Success(T value) => new(value);
    public static OperationResult<T> Fail(OperationError error, string message) => new(default, error, message);
}

public sealed record ResendResult(int NotificationId, bool IsReplay);
