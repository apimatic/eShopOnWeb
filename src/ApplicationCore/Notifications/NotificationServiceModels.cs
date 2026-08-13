using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>One requested line of a placed order: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>An order paired with the notifications recorded for it.</summary>
public record OrderWithNotifications(Order Order, IReadOnlyList<Notification> Notifications);

public enum RegisterOutcome
{
    Registered,
    Rejected
}

/// <summary>Outcome of registering a contact number.</summary>
public record RegisterContactNumberResult(RegisterOutcome Outcome, ContactNumber? ContactNumber, string? RejectionReason)
{
    public static RegisterContactNumberResult Registered(ContactNumber number) => new(RegisterOutcome.Registered, number, null);
    public static RegisterContactNumberResult Rejected(string reason) => new(RegisterOutcome.Rejected, null, reason);
}

public enum ResendOutcome
{
    /// <summary>A fresh message was sent under this idempotency key.</summary>
    Sent,

    /// <summary>This idempotency key was already used; the earlier result is returned unchanged.</summary>
    Duplicate,

    /// <summary>The message already reached the shopper, so there is nothing to re-send.</summary>
    AlreadyDelivered,

    /// <summary>The message's content was disposed of and cannot be re-sent.</summary>
    ContentDisposed,

    /// <summary>No such notification.</summary>
    NotFound
}

/// <summary>Outcome of an operator re-send.</summary>
public record ResendResult(ResendOutcome Outcome, Notification? Notification);

/// <summary>One line of the reconciliation report — a message as seen by either or both sides.</summary>
public record ReconciliationLine(
    string? Sid,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus,
    string? ProviderStatus,
    string? ProviderErrorCode,
    DateTimeOffset? ProviderDate);

/// <summary>
/// The provider's own record of messages for a date range, lined up against what eShop believes
/// it sent, so a discrepancy in either direction is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> OnlyAtProvider,
    IReadOnlyList<ReconciliationLine> OnlyInEShop);
