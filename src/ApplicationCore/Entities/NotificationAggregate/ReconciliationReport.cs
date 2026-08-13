using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A reconciliation of the provider's own record of messages against what eShop believes it sent,
/// over a date range, counting only the application's configured sending number. A message the
/// provider knows about and eShop doesn't — or the reverse — shows up in <see cref="ProviderOnly"/>
/// or <see cref="EShopOnly"/> respectively.
/// </summary>
public class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    /// <summary>The application's configured sending number the report was scoped to.</summary>
    public required string FromNumber { get; init; }

    /// <summary>Messages both the provider and eShop have a record of (matched by provider identifier).</summary>
    public List<ReconciliationEntry> Matched { get; init; } = new();

    /// <summary>Messages the provider has a record of that eShop does not.</summary>
    public List<ReconciliationEntry> ProviderOnly { get; init; } = new();

    /// <summary>Messages eShop has a record of that the provider does not (including sends the
    /// provider never accepted).</summary>
    public List<ReconciliationEntry> EShopOnly { get; init; } = new();
}

/// <summary>One line of a <see cref="ReconciliationReport"/>.</summary>
public class ReconciliationEntry
{
    /// <summary>Provider message identifier, when there is one.</summary>
    public string? ProviderSid { get; init; }

    /// <summary>eShop notification identifier, when eShop has a record.</summary>
    public int? NotificationId { get; init; }

    /// <summary>Provider (or, when provider has no record, eShop) delivery status.</summary>
    public string? Status { get; init; }

    public DateTimeOffset? SentAt { get; init; }
}
