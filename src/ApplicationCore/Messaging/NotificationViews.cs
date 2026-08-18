using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>A requested order line: which catalog item, and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Outcome of registering a contact number. On failure carries the provider's validation reasons.</summary>
public record ContactNumberRegistrationResult(bool Success, ContactNumber? ContactNumber, IReadOnlyList<string> Errors)
{
    public static ContactNumberRegistrationResult Registered(ContactNumber contactNumber) =>
        new(true, contactNumber, Array.Empty<string>());

    public static ContactNumberRegistrationResult Rejected(IReadOnlyList<string> errors) =>
        new(false, null, errors);
}

/// <summary>An order together with the notifications sent for it (with their latest delivery outcome).</summary>
public record MyOrderView(Order Order, IReadOnlyList<Notification> Notifications);

/// <summary>Result of a resend: the message it produced, and whether a new message was actually sent.</summary>
public record ResendResult(Notification Notification, bool MessageSent);

/// <summary>One line in a reconciliation report, lining a provider record up against an eShop notification.</summary>
public record ReconciliationEntry(
    string? Sid,
    int? NotificationId,
    string? ProviderStatus,
    string? EShopStatus);

/// <summary>
/// The provider's record of messages for a date range lined up against what eShop believes it sent.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
