using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

// --- Requests ---

public class CreateContactNumberRequest
{
    /// <summary>The mobile number as the caller typed it; the provider's canonical form is what gets stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class PlaceOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<PlaceOrderLine> Items { get; set; } = new();
}

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key: the same key never sends a second message; a fresh key does.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// --- Responses ---

/// <summary>A shopper's own registered number. Returned only to its owner.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

public class CreateContactNumberResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>What a message is and what became of it. Deliberately carries no phone number.</summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public bool Scheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.ProviderStatus ?? "pending",
        MessageSid = n.MessageSid,
        Scheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        ErrorCode = n.ErrorCode,
        CreatedAt = n.CreatedAt
    };
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
}

public class ReconciliationEntryDto
{
    public string MessageSid { get; set; } = string.Empty;
    public bool InProvider { get; set; }
    public bool InEShop { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? DateSentUtc { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }
    public bool ProviderResultTruncated { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> OnlyInProvider { get; set; } = new();
    public List<ReconciliationEntryDto> OnlyInEShop { get; set; } = new();
}
