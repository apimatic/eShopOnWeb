using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>A single line of a place-order request: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address supplied when placing an order.</summary>
public record AddressData(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Result of registering a contact number.</summary>
public class ContactNumberRegistrationResult
{
    public bool IsValid { get; init; }
    public int? ContactNumberId { get; init; }
    public string? CanonicalNumber { get; init; }

    public static ContactNumberRegistrationResult Registered(int id, string canonicalNumber) =>
        new() { IsValid = true, ContactNumberId = id, CanonicalNumber = canonicalNumber };

    public static ContactNumberRegistrationResult Rejected() => new() { IsValid = false };
}

/// <summary>Result of placing an order.</summary>
public class PlaceOrderResult
{
    public bool Success { get; init; }
    public int? OrderId { get; init; }
    public string? Error { get; init; }

    public static PlaceOrderResult Placed(int orderId) => new() { Success = true, OrderId = orderId };
    public static PlaceOrderResult Invalid(string error) => new() { Success = false, Error = error };
}

public enum ResendOutcome
{
    Success,
    NotFound,
    ContentDisposed
}

/// <summary>Result of an operator resend.</summary>
public class ResendResult
{
    public ResendOutcome Outcome { get; init; }
    public Notification? Notification { get; init; }

    public static ResendResult Success(Notification notification) =>
        new() { Outcome = ResendOutcome.Success, Notification = notification };

    public static ResendResult NotFound() => new() { Outcome = ResendOutcome.NotFound };
    public static ResendResult ContentDisposed() => new() { Outcome = ResendOutcome.ContentDisposed };
}

public enum DisposeContentOutcome
{
    Success,
    NotFound,
    ProviderFailed
}

/// <summary>A notification as shown to a caller reporting on an order.</summary>
public class NotificationView
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string Type { get; init; } = default!;
    public string Status { get; init; } = default!;
    public string? ProviderMessageSid { get; init; }
    public bool ContentRedacted { get; init; }
    public DateTimeOffset CreatedDate { get; init; }
    public DateTimeOffset? ScheduledSendAt { get; init; }

    public static NotificationView From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        ContentRedacted = n.ContentRedacted,
        CreatedDate = n.CreatedDate,
        ScheduledSendAt = n.ScheduledSendAt
    };
}

/// <summary>An order together with where each of its notifications got to.</summary>
public class OrderNotificationsView
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public decimal Total { get; init; }
    public IReadOnlyList<NotificationView> Notifications { get; init; } = new List<NotificationView>();
}

/// <summary>Result of fetching the notifications for a single order (with ownership enforced).</summary>
public class OrderNotificationsResult
{
    public bool Found { get; init; }
    public IReadOnlyList<NotificationView> Notifications { get; init; } = new List<NotificationView>();

    public static OrderNotificationsResult NotFound() => new() { Found = false };
    public static OrderNotificationsResult Of(IReadOnlyList<NotificationView> notifications) =>
        new() { Found = true, Notifications = notifications };
}

/// <summary>A reconciliation entry lining up the provider's record against eShop's.</summary>
public class ReconciliationEntry
{
    public string? Sid { get; init; }
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
}

/// <summary>
/// A reconciliation report over a date range: the provider's own record of messages sent from this
/// application's configured From number, lined up against what eShop believes it sent.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = default!;

    /// <summary>Messages both the provider and eShop know about (joined by SID).</summary>
    public IReadOnlyList<ReconciliationEntry> Matched { get; init; } = new List<ReconciliationEntry>();

    /// <summary>Messages the provider knows about but eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; init; } = new List<ReconciliationEntry>();

    /// <summary>Messages eShop believes it sent but the provider's list does not contain.</summary>
    public IReadOnlyList<ReconciliationEntry> EShopOnly { get; init; } = new List<ReconciliationEntry>();
}
