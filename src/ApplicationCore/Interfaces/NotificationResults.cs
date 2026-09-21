using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Result of registering a contact number (validation may reject it).</summary>
public record RegisterContactNumberResult(bool Succeeded, int? ContactNumberId, string? CanonicalNumber, string? Error);

/// <summary>A contact number as returned to its owner (never includes anything that isn't the number itself).</summary>
public record ContactNumberView(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedDate);

/// <summary>One notification's state, including where its provider message got to.</summary>
public record OrderNotificationView(
    int NotificationId,
    int OrderId,
    NotificationType Type,
    string? ProviderMessageSid,
    string? DeliveryStatus,
    bool IsScheduledFollowUp,
    bool FollowUpCanceled,
    bool ContentRedacted,
    bool SendFailed,
    string? FailureReason,
    DateTimeOffset CreatedDate);

/// <summary>An order in the caller's list, with where each of its notifications got to.</summary>
public record MyOrderView(
    int OrderId,
    OrderLifecycleStatus? Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderNotificationView> Notifications);

/// <summary>Outcome of an order lifecycle transition (place is separate; this covers dispatch/cancel).</summary>
public enum OrderTransitionOutcome
{
    Succeeded = 0,
    OrderNotFound = 1,
    /// <summary>Already in the target state — nothing sent (no-op).</summary>
    NoOp = 2,
    /// <summary>The transition is not allowed from the current state (e.g. dispatch after cancel).</summary>
    InvalidState = 3
}

public record OrderTransitionResult(OrderTransitionOutcome Outcome, string? Message = null);

/// <summary>Outcome of an operator resend.</summary>
public record ResendResult(bool Succeeded, int? NotificationId, bool Duplicate, string? Error);

/// <summary>One line of the reconciliation report; matched lines carry both sides.</summary>
public record ReconciliationLine(
    string Sid,
    string? ProviderStatus,
    string? AppStatus,
    int? NotificationId,
    int? OrderId);

/// <summary>
/// Provider's record for the range lined up against what eShop believes it sent, both keyed by SID.
/// Phone numbers are deliberately excluded.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationLine> Matched,
    IReadOnlyList<ReconciliationLine> ProviderOnly,
    IReadOnlyList<ReconciliationLine> EShopOnly,
    bool ProviderListTruncated);
