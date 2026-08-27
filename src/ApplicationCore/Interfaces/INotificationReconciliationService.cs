using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface INotificationReconciliationService
{
    /// <summary>
    /// Lines up the provider's own record of messages (from this application's sending number
    /// only) against what eShop believes it sent, for a date range.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }
    public List<ReconciliationEntry> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about from our sending number that eShop has no record of.</summary>
    public List<SmsMessageState> OnlyInProvider { get; set; } = new();

    /// <summary>Notifications eShop recorded that the provider has no record of in this range.</summary>
    public List<ReconciliationLocalEntry> OnlyInEShop { get; set; } = new();
}

public class ReconciliationEntry
{
    public int NotificationId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public bool StatusMismatch { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationLocalEntry
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
