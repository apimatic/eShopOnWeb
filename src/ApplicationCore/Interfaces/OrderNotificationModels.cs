using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item and quantity to place on an order.</summary>
public record OrderItemInput(int CatalogItemId, int Quantity);

/// <summary>An optional shipping address for a placed order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>What became of one notification message. The destination number is deliberately omitted.</summary>
public record NotificationView(
    int NotificationId,
    int OrderId,
    string Type,
    string? ProviderMessageSid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    bool ContentDisposed,
    string? Content,
    DateTimeOffset CreatedDate,
    DateTimeOffset? ScheduledFor);

/// <summary>An order the caller owns, and where each of its notifications got to.</summary>
public record OrderSummary(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<NotificationView> Notifications);

/// <summary>One line of a reconciliation report.</summary>
public record ReconciliationEntry(
    string? MessageSid,
    string Status,
    int? NotificationId,
    int? OrderId);

/// <summary>
/// The provider's own record of messages for a range, lined up against what eShop believes it sent.
/// A message the provider knows about but eShop doesn't appears in <see cref="ProviderOnly"/>; the
/// reverse appears in <see cref="EShopOnly"/>.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
