using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>One requested order line: a catalog item id and how many of it.</summary>
public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Optional shipping address on a place-order request.</summary>
public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>What a caller sees about a single SMS: its id (what operator endpoints act on) and where it got to.</summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ProviderMessageSid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentDisposed { get; set; }
    public string? FailureReason { get; set; }
    /// <summary>Destination shown masked; a shopper's full number is never surfaced.</summary>
    public string? Destination { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Type = n.Type.ToString(),
        DeliveryStatus = n.DeliveryStatus,
        ErrorCode = n.ErrorCode,
        ProviderMessageSid = n.ProviderMessageSid,
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor,
        ContentDisposed = n.ContentDisposed,
        FailureReason = n.FailureReason,
        Destination = Mask(n.ToNumber)
    };

    private static string? Mask(string? e164)
    {
        if (string.IsNullOrEmpty(e164))
            return e164;
        if (e164.Length <= 4)
            return new string('*', e164.Length);
        return string.Concat(e164.AsSpan(0, 2), new string('*', e164.Length - 4), e164.AsSpan(e164.Length - 2));
    }
}

/// <summary>A caller's order with where its notifications got to.</summary>
public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
