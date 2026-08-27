using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    /// <summary>
    /// Tells the shopper their order was placed. Never throws for messaging
    /// failures; a shopper with no number on file is simply not messaged.
    /// </summary>
    Task NotifyOrderPlacedAsync(Order order);

    /// <summary>
    /// Tells the shopper their order is on its way and queues a delivery
    /// follow-up message with the provider for a few days later.
    /// Never throws for messaging failures.
    /// </summary>
    Task NotifyOrderDispatchedAsync(Order order);

    /// <summary>
    /// Tells the shopper their order was cancelled and cancels any follow-up
    /// message that has not yet gone out so it never reaches them.
    /// Never throws for messaging failures.
    /// </summary>
    Task NotifyOrderCancelledAsync(Order order);

    /// <summary>
    /// The caller's own orders, each with where its notifications got to.
    /// </summary>
    Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId);

    /// <summary>
    /// What was sent for an order and what became of each message, refreshing
    /// delivery outcomes from the provider. Returns null when no such order
    /// exists for the caller (shoppers may only see their own orders).
    /// </summary>
    Task<IReadOnlyList<OrderNotification>?> GetOrderNotificationsAsync(int orderId, string callerId, bool isOperator);

    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request
    /// under the same idempotency key returns the message the first attempt
    /// produced without sending again. Returns null when no such notification
    /// exists.
    /// </summary>
    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, string operatorId);

    /// <summary>
    /// Disposes of the content of a message, both at the provider and locally,
    /// while keeping the record that a message was sent and what became of it.
    /// Returns null when no such notification exists.
    /// </summary>
    Task<OrderNotification?> DisposeContentAsync(int notificationId);

    /// <summary>
    /// Lines up the provider's own record of messages sent from this
    /// application's sending number against what eShop believes it sent.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to);
}
