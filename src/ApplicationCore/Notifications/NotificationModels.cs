using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public class OrderLineRequest
{
    public OrderLineRequest(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public int CatalogItemId { get; }
    public int Quantity { get; }
}

/// <summary>An order paired with the notifications raised for it.</summary>
public class OrderWithNotifications
{
    public OrderWithNotifications(Order order, IReadOnlyList<OrderNotification> notifications)
    {
        Order = order;
        Notifications = notifications;
    }

    public Order Order { get; }
    public IReadOnlyList<OrderNotification> Notifications { get; }
}

/// <summary>One line of a reconciliation report.</summary>
public class ReconciliationEntry
{
    public string Sid { get; set; } = string.Empty;

    /// <summary>Provider's status, when the provider knows the message.</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>eShop's recorded status, when eShop knows the message.</summary>
    public string? EShopStatus { get; set; }

    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    /// <summary>Where the message is known: both, provider only, or eShop only.</summary>
    public string Presence { get; set; } = string.Empty;
}

/// <summary>
/// A reconciliation of the provider's own record of messages (from the configured sending number)
/// against what eShop believes it sent, over a date range.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The sending number the provider was asked about (Twilio:FromNumber).</summary>
    public string? FromNumber { get; set; }

    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    /// <summary>Messages both the provider and eShop know about.</summary>
    public List<ReconciliationEntry> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about but eShop does not.</summary>
    public List<ReconciliationEntry> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent but the provider did not return for the range.</summary>
    public List<ReconciliationEntry> EShopOnly { get; set; } = new();
}
