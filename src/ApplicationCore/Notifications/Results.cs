using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>Why a place-order request could not be honoured, if it could not.</summary>
public enum PlaceOrderError
{
    None = 0,
    NoItems = 1,
    ItemNotFound = 2
}

/// <summary>Outcome of placing an order.</summary>
public record PlaceOrderResult(int? OrderId, PlaceOrderError Error)
{
    public bool Succeeded => Error == PlaceOrderError.None && OrderId.HasValue;

    public static PlaceOrderResult Success(int orderId) => new(orderId, PlaceOrderError.None);
    public static PlaceOrderResult Failure(PlaceOrderError error) => new(null, error);
}

/// <summary>Outcome of an operator action against an order (dispatch / cancel).</summary>
public enum OrderOperationOutcome
{
    Success = 0,
    NotFound = 1,
    InvalidState = 2
}

/// <summary>Why a resend could not proceed, if it could not.</summary>
public enum ResendOutcome
{
    Sent = 0,
    Duplicate = 1,
    NotFound = 2,
    ContentDisposed = 3
}

/// <summary>Outcome of a resend request.</summary>
public record ResendResult(ResendOutcome Outcome, int? NotificationId)
{
    public static ResendResult NotFound => new(ResendOutcome.NotFound, null);
    public static ResendResult ContentDisposed => new(ResendOutcome.ContentDisposed, null);
    public static ResendResult Sent(int notificationId) => new(ResendOutcome.Sent, notificationId);
    public static ResendResult Duplicate(int notificationId) => new(ResendOutcome.Duplicate, notificationId);
}

/// <summary>A message about an order and what became of it.</summary>
public record OrderNotificationView(
    int NotificationId,
    int OrderId,
    NotificationKind Kind,
    NotificationDeliveryStatus Status,
    string? ProviderMessageSid,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledSendAt,
    bool ContentDisposed);

/// <summary>One of a shopper's orders together with where its notifications got to.</summary>
public record OrderSummary(
    int OrderId,
    OrderStatus Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderNotificationView> Notifications);

/// <summary>One line of a reconciliation report: a message and where each side stands on it.</summary>
public record ReconciliationEntry(
    string ProviderMessageSid,
    NotificationDeliveryStatus? ProviderStatus,
    NotificationDeliveryStatus? EShopStatus,
    int? NotificationId,
    bool KnownToProvider,
    bool KnownToEShop);

/// <summary>
/// A reconciliation report over a date range: the provider's record of messages sent from the
/// application's own sending number, lined up against what eShop believes it sent.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EShopMessageCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
