using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>A shopper's registered number. Returned only to its owner.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset DateRegistered { get; set; }

    public static ContactNumberDto From(ContactNumber c) => new()
    {
        ContactNumberId = c.Id,
        PhoneNumber = c.PhoneNumber,
        DateRegistered = c.DateRegistered
    };
}

/// <summary>
/// What was sent for an order and what became of it. The destination number is deliberately absent:
/// it is PII and is never returned. <see cref="NotificationId"/> is what the operator endpoints act on.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    /// <summary>The last-known delivery outcome (the provider owns this).</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>The provider's own identifier for the message, when it accepted one.</summary>
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.ProviderStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ContentRedacted = n.ContentRedacted,
        ScheduledSendAt = n.ScheduledSendAt,
        CreatedAt = n.CreatedAt
    };
}

/// <summary>A shopper's order together with where its notifications got to.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>One message when reconciling the provider's ledger against eShop's records.</summary>
public class ReconciliationEntryDto
{
    public string MessageSid { get; set; } = string.Empty;
    public bool InProvider { get; set; }
    public bool InEShop { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry e) => new()
    {
        MessageSid = e.MessageSid,
        InProvider = e.InProvider,
        InEShop = e.InEShop,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        ProviderDateSent = e.ProviderDateSent,
        NotificationId = e.NotificationId,
        OrderId = e.OrderId
    };
}
