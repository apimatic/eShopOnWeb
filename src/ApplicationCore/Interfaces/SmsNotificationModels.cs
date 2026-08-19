using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineInput(int CatalogItemId, int Units);

/// <summary>Outcome of registering a contact number.</summary>
public record ContactNumberRegistrationResult(bool Success, ContactNumber? ContactNumber, IReadOnlyList<string> Errors)
{
    public static ContactNumberRegistrationResult Ok(ContactNumber number) =>
        new(true, number, Array.Empty<string>());

    public static ContactNumberRegistrationResult Rejected(IReadOnlyList<string> errors) =>
        new(false, null, errors);
}

/// <summary>Outcome of placing an order.</summary>
public record PlaceOrderResult(bool Success, int OrderId, string? Error)
{
    public static PlaceOrderResult Placed(int orderId) => new(true, orderId, null);
    public static PlaceOrderResult Invalid(string error) => new(false, 0, error);
}

/// <summary>A single notification as shown by the API (destination is masked).</summary>
public record NotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string Status,
    string? ProviderStatus,
    string? ProviderMessageSid,
    int? ErrorCode,
    string? ErrorMessage,
    bool ContentRedacted,
    DateTimeOffset? ScheduledSendAt,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedDate,
    string Destination);

public record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>An order plus where each of its notifications got to.</summary>
public record OrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderItemView> Items,
    IReadOnlyList<NotificationView> Notifications);

/// <summary>Result of an operator resend, carrying the identifier of the message it produced.</summary>
public record ResendResult(bool Found, int NotificationId, string? Note)
{
    public static ResendResult NotFound() => new(false, 0, null);
    public static ResendResult Produced(int notificationId, string? note = null) => new(true, notificationId, note);
}

/// <summary>One line of the reconciliation report: a message as the two sides see it.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? EShopStatus,
    string? Destination,
    DateTimeOffset? DateSent);

/// <summary>
/// The provider's own record of messages for a range lined up against what eShop believes it
/// sent, so a message one side knows about and the other does not is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string SendingNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
