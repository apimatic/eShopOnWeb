using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The result of reconciling the provider's record of messages (from the configured sending
/// number, over a date range) against what this application believes it sent.
/// </summary>
public class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    /// <summary>Messages both the provider and this application agree on, keyed by provider id.</summary>
    public List<ReconciliationMatch> Matched { get; init; } = new();

    /// <summary>Messages the provider has a record of that this application does not.</summary>
    public List<ReconciliationProviderRecord> ProviderOnly { get; init; } = new();

    /// <summary>Messages this application believes it sent that the provider has no record of.</summary>
    public List<ReconciliationEShopRecord> EShopOnly { get; init; } = new();
}

public record ReconciliationMatch(
    string ProviderMessageSid,
    int NotificationId,
    int OrderId,
    string ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? DateSent);

public record ReconciliationProviderRecord(
    string ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset? DateSent);

public record ReconciliationEShopRecord(
    int NotificationId,
    int OrderId,
    string? ProviderMessageSid,
    string ProviderStatus);
