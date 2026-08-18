using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Outcome of resending a message.</summary>
public record ResendOutcome(int NotificationId, bool Resent);

/// <summary>One line of the reconciliation report: a provider message the provider knows about.</summary>
public record ReconciliationProviderEntry(
    string Sid,
    string? Status,
    string? From,
    string? MaskedTo,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    bool KnownToEShop);

/// <summary>One line of the reconciliation report: a message eShop believes it sent.</summary>
public record ReconciliationEShopEntry(
    int NotificationId,
    int OrderId,
    string? Sid,
    string? Status,
    DateTimeOffset? SentAt,
    bool KnownToProvider);

/// <summary>
/// The provider's own record of messages for a date range, lined up against what eShop believes
/// it sent, so a message one side knows about and the other doesn't is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<ReconciliationProviderEntry> ProviderMessages,
    IReadOnlyList<string> InProviderNotInEShop,
    IReadOnlyList<ReconciliationEShopEntry> InEShopNotInProvider);
