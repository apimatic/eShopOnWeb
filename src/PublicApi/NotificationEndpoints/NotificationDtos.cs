using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>A registered contact number, as returned to its owner.</summary>
public class ContactNumberDto
{
    public int ContactNumberId { get; init; }
    public string PhoneNumber { get; init; } = default!;
    public DateTimeOffset CreatedDate { get; init; }
}

/// <summary>
/// A single notification and what became of it. Carries the provider's identifier and current delivery
/// outcome so operator endpoints can act on it. The message body and the full destination number are
/// never exposed (the destination is masked).
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string Kind { get; init; } = default!;
    public string State { get; init; } = default!;
    public string? MaskedTo { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public int? ProviderErrorCode { get; init; }
    public string? ProviderErrorMessage { get; init; }
    public bool ContentRedacted { get; init; }
    public DateTimeOffset? ScheduledFor { get; init; }
    public DateTimeOffset CreatedDate { get; init; }
    public DateTimeOffset? UpdatedDate { get; init; }
    public int? ResendOfNotificationId { get; init; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = default!;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
}

/// <summary>An order and where each of its notifications got to.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string Status { get; init; } = default!;
    public decimal Total { get; init; }
    public List<OrderItemDto> Items { get; init; } = new();
    public List<NotificationDto> Notifications { get; init; } = new();
}

/// <summary>Maps notification-feature entities to their API DTOs. Never surfaces message bodies or full numbers.</summary>
public static class NotificationDtoMappers
{
    public static ContactNumberDto ToDto(this ContactNumber contactNumber) => new()
    {
        ContactNumberId = contactNumber.Id,
        PhoneNumber = contactNumber.PhoneNumber,
        CreatedDate = contactNumber.CreatedDate
    };

    public static NotificationDto ToDto(this OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        State = notification.State.ToString(),
        MaskedTo = MaskNumber(notification.ToPhoneNumber),
        ProviderMessageSid = notification.ProviderMessageSid,
        ProviderStatus = notification.ProviderStatus,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        ContentRedacted = notification.ContentRedacted,
        ScheduledFor = notification.ScheduledFor,
        CreatedDate = notification.CreatedDate,
        UpdatedDate = notification.UpdatedDate,
        ResendOfNotificationId = notification.ResendOfNotificationId
    };

    /// <summary>Masks a number to its last four digits so it can appear in a response without exposing it.</summary>
    public static string? MaskNumber(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return null;
        }

        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? "••••" : "••••••" + digits[^4..];
    }
}
