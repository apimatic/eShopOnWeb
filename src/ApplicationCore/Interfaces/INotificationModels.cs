using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A registered contact number, as returned to its owner.</summary>
public record ContactNumberDto(int ContactNumberId, string PhoneNumber, string? CountryCode, DateTimeOffset RegisteredAtUtc);

/// <summary>A single line of a new order: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>One message about an order and what became of it.</summary>
public record NotificationSummary(
    int NotificationId,
    string Kind,
    string? Status,
    string? MessageSid,
    bool IsScheduled,
    bool ContentRedacted,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentAtUtc);

/// <summary>An order with where each of its notifications got to.</summary>
public record OrderSummary(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<NotificationSummary> Notifications);

/// <summary>Outcome of an operator transition (dispatch/cancel).</summary>
public enum OrderActionStatus
{
    Applied = 0,
    AlreadyInState = 1,
    NotFound = 2
}

public record OrderActionResult(OrderActionStatus Status, OrderSummary? Order);

/// <summary>Outcome of an operator re-send.</summary>
public record ResendResult(bool NotificationFound, int? NotificationId, bool WasDuplicate);

/// <summary>How a message lines up between the provider's record and eShop's.</summary>
public enum ReconciliationMatch
{
    Matched = 0,
    OnlyAtProvider = 1,
    OnlyAtEShop = 2
}

public record ReconciliationEntry(
    string? MessageSid,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId,
    ReconciliationMatch Match);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Complete,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<ReconciliationEntry> Entries);
