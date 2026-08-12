using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>Outcome of registering a shopper contact number.</summary>
public record RegisterContactNumberResult(bool Succeeded, int ContactNumberId, string? CanonicalE164, string? Error)
{
    public static RegisterContactNumberResult Ok(int id, string canonical) => new(true, id, canonical, null);
    public static RegisterContactNumberResult Rejected(string reason) => new(false, 0, null, reason);
}

/// <summary>Outcome of an operator resend.</summary>
public record ResendResult(bool Found, int NotificationId, bool Sent, string? Error)
{
    public static ResendResult NotFound() => new(false, 0, false, "Notification not found.");
    public static ResendResult Fresh(int notificationId) => new(true, notificationId, true, null);
    public static ResendResult Duplicate(int notificationId) => new(true, notificationId, false, null);
    public static ResendResult Invalid(int notificationId, string error) => new(true, notificationId, false, error);
}

public enum ReconciliationDiscrepancy
{
    /// <summary>Present at both the provider and in eShop's records.</summary>
    Matched = 0,
    /// <summary>The provider knows about this message but eShop has no record of it.</summary>
    ProviderOnly = 1,
    /// <summary>eShop believes it sent this but the provider does not report it in the range.</summary>
    EShopOnly = 2
}

/// <summary>One reconciled message, lined up between the provider and eShop.</summary>
public record ReconciliationEntry(
    string? Sid,
    ReconciliationDiscrepancy Discrepancy,
    string? ProviderStatus,
    string? EShopStatus,
    int? OrderId,
    NotificationKind? Kind,
    DateTimeOffset? DateSent);

/// <summary>A reconciliation report over a date range, counting only eShop's own sending number.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationEntry> Entries);
